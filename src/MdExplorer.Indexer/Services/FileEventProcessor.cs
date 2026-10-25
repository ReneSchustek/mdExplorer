using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Indexer.Abstractions;
using MdExplorer.Indexer.Models;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Indexer.Services;

/// <summary>
/// Überträgt einen Stapel Watcher-Ereignisse in den Index.
/// </summary>
/// <remarks>
/// <para>
/// Für die Ereignisse eines Stapels gilt ein gemeinsamer Stand: Wer eine Datei in diesem Stapel
/// angelegt oder verschoben hat, wird nicht noch einmal in der Datenbank gesucht. Die Datenbank
/// sieht Vorgemerktes erst nach dem Speichern; zwei Ereignisse zur selben neuen Datei hätten sonst
/// beide „neu“ gesehen und zweimal angelegt.
/// </para>
/// <para>
/// Ereignisse, die die ganze Wurzel betreffen (Pufferüberlauf, geänderte <c>.mdignore</c>), werden
/// nicht hier abgeglichen, sondern als Wurzel zurückgegeben. Der Aufrufer gleicht sie nach dem
/// Speichern dieses Stapels ab.
/// </para>
/// </remarks>
internal sealed partial class FileEventProcessor
{
    private const string MarkdownExtension = ".md";

    private readonly IFileScanner _scanner;
    private readonly IndexEntryWriter _writer;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger _logger;

    public FileEventProcessor(IFileScanner scanner, IndexEntryWriter writer, IFileSystem fileSystem, ILogger logger)
    {
        _scanner = scanner;
        _writer = writer;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>Wendet die Ereignisse an und liefert die Wurzeln, die vollständig abzugleichen sind.</summary>
    public async Task<IReadOnlyCollection<string>> ApplyAsync(
        IMarkdownFileRepository repository,
        IReadOnlyList<FileSystemEvent> batch,
        CancellationToken cancellationToken)
    {
        BatchState state = new(repository);
        foreach (FileSystemEvent fileSystemEvent in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ApplyAsync(state, fileSystemEvent, cancellationToken).ConfigureAwait(false);
        }
        return state.RootsToSync;
    }

    [LoggerMessage(EventId = 109, Level = LogLevel.Debug, Message = "Datei entfernt: {Path}")]
    private static partial void LogFileRemoved(ILogger logger, string path);

    [LoggerMessage(EventId = 122, Level = LogLevel.Information, Message = "Ordner {OldPath} → {NewPath}: {Count} Eintrag/Einträge mitgenommen.")]
    private static partial void LogDirectoryMoved(ILogger logger, string oldPath, string newPath, int count);

    [LoggerMessage(EventId = 123, Level = LogLevel.Information, Message = "Ordner {Path} gelöscht: {Count} Eintrag/Einträge entfernt.")]
    private static partial void LogDirectoryRemoved(ILogger logger, string path, int count);

    private Task ApplyAsync(BatchState state, FileSystemEvent fileSystemEvent, CancellationToken cancellationToken)
    {
        switch (fileSystemEvent.Kind)
        {
            case FileSystemEventKind.Created:
            case FileSystemEventKind.Changed:
                return UpsertAsync(state, fileSystemEvent.Path, fileSystemEvent.Root, cancellationToken);
            case FileSystemEventKind.Deleted:
                return RemoveAsync(state, fileSystemEvent.Path, cancellationToken);
            case FileSystemEventKind.Renamed:
                return RenameAsync(state, fileSystemEvent, cancellationToken);
            case FileSystemEventKind.DirectoryRenamed:
                return MoveDirectoryAsync(state, fileSystemEvent, cancellationToken);
            case FileSystemEventKind.DirectoryDeleted:
                return RemoveDirectoryAsync(state, fileSystemEvent.Path, cancellationToken);
            case FileSystemEventKind.IgnoreFileChanged:
                // Die neue Regel gilt sofort; der Abgleich der Wurzel nimmt aus, was sie jetzt trifft,
                // und nimmt auf, was sie freigibt.
                _scanner.InvalidateExclusions();
                _ = state.RootsToSync.Add(fileSystemEvent.Root);
                return Task.CompletedTask;
            case FileSystemEventKind.ResyncRequested:
                _ = state.RootsToSync.Add(fileSystemEvent.Root);
                return Task.CompletedTask;
            default:
                throw new InvalidOperationException($"Unbekannter Ereignistyp: {fileSystemEvent.Kind}.");
        }
    }

    /// <summary>
    /// Pausierte Ordner, Ausschlussmuster und alles außerhalb der Wurzel gelten auch für Ereignisse.
    /// Sonst stünde jede Datei aus <c>node_modules</c> bis zum nächsten Abgleich im Index.
    /// </summary>
    private bool IsIndexable(string path, string root) =>
        path.EndsWith(MarkdownExtension, StringComparison.OrdinalIgnoreCase)
            && PathScope.IsBelow(path, root)
            && !_scanner.IsExcluded(path, root);

    private async Task UpsertAsync(BatchState state, string path, string root, CancellationToken cancellationToken)
    {
        if (!IsIndexable(path, root) || _writer.TryStamp(path) is not { } stamp)
        {
            return;
        }

        MarkdownFile? existing = await state.FindAsync(path, cancellationToken).ConfigureAwait(false);
        (IndexChange change, MarkdownFile? entry) = await _writer
            .UpsertAsync(state.Repository, path, root, existing, stamp, cancellationToken)
            .ConfigureAwait(false);
        if (change == IndexChange.Added)
        {
            state.Known[path] = entry;
        }
    }

    private async Task RemoveAsync(BatchState state, string path, CancellationToken cancellationToken)
    {
        MarkdownFile? existing = await state.FindAsync(path, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }
        state.Repository.Remove(existing);
        state.Known[path] = null;
        LogFileRemoved(_logger, path);
    }

    private async Task RenameAsync(BatchState state, FileSystemEvent rename, CancellationToken cancellationToken)
    {
        string? oldPath = rename.OldPath;
        if (string.IsNullOrWhiteSpace(oldPath))
        {
            await UpsertAsync(state, rename.Path, rename.Root, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!IsIndexable(rename.Path, rename.Root))
        {
            await LeaveIndexAsync(state, oldPath, rename.Root, cancellationToken).ConfigureAwait(false);
            return;
        }

        MarkdownFile? moving = await state.FindAsync(oldPath, cancellationToken).ConfigureAwait(false);
        if (moving is null || _writer.TryStamp(rename.Path) is not { } stamp)
        {
            await UpsertAsync(state, rename.Path, rename.Root, cancellationToken).ConfigureAwait(false);
            await RemoveAsync(state, oldPath, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Speichern über eine Umbenennung auf eine schon indizierte Datei: Deren Eintrag weicht,
        // sonst stünde derselbe Pfad zweimal im Index.
        MarkdownFile? replaced = await state.FindAsync(rename.Path, cancellationToken).ConfigureAwait(false);
        if (replaced is not null && replaced.Id != moving.Id)
        {
            state.Repository.Remove(replaced);
        }

        _writer.Move(moving, rename.Path, rename.Root);
        state.Known[oldPath] = null;
        state.Known[rename.Path] = moving;
        await _writer.RefreshMovedAsync(state.Repository, moving, stamp, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Die Datei wurde auf einen Namen umbenannt, der nicht in den Index gehört: eine
    /// Sicherungsdatei (Name mit angehängter Tilde), ein Ziel außerhalb der Wurzel oder in einem ausgeschlossenen
    /// Ordner.
    /// </summary>
    /// <remarks>
    /// Editoren mit Sicherungsdatei speichern so: die Notiz auf den Namen mit Tilde umbenennen, dann
    /// die neue Fassung unter dem alten Namen. Ginge der Eintrag mit auf die Sicherungsdatei über,
    /// entstünde für die Notiz ein
    /// neuer mit neuer Id, und Dokument und Schlagwort-Zuordnungen fielen bei jedem Speichern weg.
    /// Steht am alten Pfad also schon wieder eine Datei, bleibt der Eintrag und wird aufgefrischt.
    /// </remarks>
    private Task LeaveIndexAsync(BatchState state, string oldPath, string root, CancellationToken cancellationToken) =>
        _fileSystem.FileExists(oldPath)
            ? UpsertAsync(state, oldPath, root, cancellationToken)
            : RemoveAsync(state, oldPath, cancellationToken);

    /// <summary>Nimmt die Einträge eines umbenannten oder verschobenen Ordners mit; ihre Ids bleiben.</summary>
    private async Task MoveDirectoryAsync(BatchState state, FileSystemEvent move, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(move.OldPath))
        {
            _ = state.RootsToSync.Add(move.Root);
            return;
        }

        string oldDirectory = PathScope.TrimSeparators(move.OldPath);
        string newDirectory = PathScope.TrimSeparators(move.Path);
        IReadOnlyList<MarkdownFile> entries = await state.Repository
            .GetAllUnderRootAsync(oldDirectory, cancellationToken)
            .ConfigureAwait(false);
        foreach (MarkdownFile listed in entries)
        {
            MarkdownFile entry = state.Tracked(listed);
            string oldPath = entry.AbsolutePath;
            string newPath = newDirectory + oldPath[oldDirectory.Length..];
            state.Known[oldPath] = null;
            if (!IsIndexable(newPath, move.Root))
            {
                state.Repository.Remove(entry);
                continue;
            }
            _writer.Move(entry, newPath, move.Root);
            state.Repository.Update(entry);
            state.Known[newPath] = entry;
        }
        LogDirectoryMoved(_logger, oldDirectory, newDirectory, entries.Count);
    }

    private async Task RemoveDirectoryAsync(BatchState state, string directory, CancellationToken cancellationToken)
    {
        IReadOnlyList<MarkdownFile> entries = await state.Repository
            .GetAllUnderRootAsync(PathScope.TrimSeparators(directory), cancellationToken)
            .ConfigureAwait(false);
        foreach (MarkdownFile listed in entries)
        {
            MarkdownFile entry = state.Tracked(listed);
            state.Repository.Remove(entry);
            state.Known[entry.AbsolutePath] = null;
        }
        LogDirectoryRemoved(_logger, directory, entries.Count);
    }

    /// <summary>Der gemeinsame Stand der Ereignisse eines Stapels.</summary>
    private sealed class BatchState(IMarkdownFileRepository repository)
    {
        public IMarkdownFileRepository Repository { get; } = repository;

        /// <summary>Was dieser Stapel über einen Pfad schon weiß; <see langword="null"/> heißt: kein Eintrag.</summary>
        public Dictionary<string, MarkdownFile?> Known { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> RootsToSync { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Die Instanz, die dieser Stapel schon zu dem Eintrag kennt. Zwei Instanzen mit derselben Id
        /// im selben Kontext weist EF Core beim Vormerken ab.
        /// </summary>
        public MarkdownFile Tracked(MarkdownFile listed) =>
            Known.TryGetValue(listed.AbsolutePath, out MarkdownFile? known) && known is not null && known.Id == listed.Id
                ? known
                : listed;

        public async Task<MarkdownFile?> FindAsync(string path, CancellationToken cancellationToken)
        {
            if (Known.TryGetValue(path, out MarkdownFile? known))
            {
                return known;
            }
            MarkdownFile? found = await Repository.GetByAbsolutePathAsync(path, cancellationToken).ConfigureAwait(false);
            Known[path] = found;
            return found;
        }
    }
}
