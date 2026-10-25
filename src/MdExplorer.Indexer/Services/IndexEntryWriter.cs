using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Indexer.Abstractions;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Indexer.Services;

/// <summary>Was ein Schreibschritt am Index geändert hat.</summary>
internal enum IndexChange
{
    /// <summary>Nichts: Größe und Änderungszeit stimmten, oder die Datei war nicht lesbar.</summary>
    None,

    /// <summary>Ein neuer Eintrag.</summary>
    Added,

    /// <summary>Neuer Inhalt.</summary>
    Updated,

    /// <summary>Gleicher Inhalt, aber neue Größe oder Änderungszeit.</summary>
    Touched,
}

/// <summary>Größe und Änderungszeit einer Datei, gelesen in einem Zug.</summary>
/// <param name="SizeBytes">Größe in Byte.</param>
/// <param name="LastWriteTimeUtc">Letzte Änderung in UTC.</param>
internal readonly record struct FileStamp(long SizeBytes, DateTime LastWriteTimeUtc);

/// <summary>
/// Legt Einträge an, frischt sie auf und verschiebt sie. Abgleich und Ereignisverarbeitung
/// benutzen denselben Weg, damit eine Datei unabhängig davon gleich behandelt wird, wer sie meldet.
/// </summary>
internal sealed partial class IndexEntryWriter
{
    // Ein Editor hält die Datei beim Speichern oft kurz gesperrt. Drei Versuche mit 50 und
    // 100 ms Pause überbrücken das; danach wird die Datei übersprungen und gewarnt.
    private const int MaxHashAttempts = 3;

    private readonly IHashCalculator _hashCalculator;
    private readonly IFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    public IndexEntryWriter(IHashCalculator hashCalculator, IFileSystem fileSystem, TimeProvider timeProvider, ILogger logger)
    {
        _hashCalculator = hashCalculator;
        _fileSystem = fileSystem;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Liest Größe und Änderungszeit; <see langword="null"/>, wenn die Datei inzwischen fehlt.
    /// </summary>
    /// <remarks>
    /// Zwischen dem Finden einer Datei und dem Lesen ihrer Größe kann ein Editor sie wieder
    /// löschen. Das kostet nur diese Datei; ein Abbruch des ganzen Durchgangs ließe alle übrigen
    /// aus und das Aufräumen ausfallen.
    /// </remarks>
    public FileStamp? TryStamp(string path)
    {
        try
        {
            return _fileSystem.FileExists(path)
                ? new FileStamp(_fileSystem.GetFileSize(path), _fileSystem.GetLastWriteTimeUtc(path))
                : null;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            LogFileVanished(_logger, path);
            return null;
        }
    }

    /// <summary>Bringt den Eintrag einer Datei auf den Stand der Platte.</summary>
    /// <returns>Die Änderung und, bei einer Neuanlage, der neue Eintrag.</returns>
    public async Task<(IndexChange Change, MarkdownFile? Entry)> UpsertAsync(
        IMarkdownFileRepository repository,
        string path,
        string root,
        MarkdownFile? existing,
        FileStamp stamp,
        CancellationToken cancellationToken)
    {
        // Gleiche Größe und Änderungszeit gelten als unverändert, der Hash wird gar nicht erst
        // berechnet. Davon lebt der Abgleich, der jede Datei jeder Wurzel erneut anbietet.
        if (existing is not null && existing.SizeBytes == stamp.SizeBytes && existing.LastWriteTimeUtc == stamp.LastWriteTimeUtc)
        {
            return (IndexChange.None, existing);
        }

        string? hash = await ComputeHashWithRetryAsync(path, cancellationToken).ConfigureAwait(false);
        if (hash is null)
        {
            return (IndexChange.None, existing);
        }

        if (existing is null)
        {
            MarkdownFile created = Create(path, root, stamp, hash);
            await repository.AddAsync(created, cancellationToken).ConfigureAwait(false);
            LogFileAdded(_logger, path);
            return (IndexChange.Added, created);
        }

        return (ApplyContent(repository, existing, stamp, hash), existing);
    }

    /// <summary>Trägt den neuen Ort ein; die Id bleibt, und mit ihr alles, was an der Datei hängt.</summary>
    public void Move(MarkdownFile entry, string newPath, string root)
    {
        LogFileRenamed(_logger, entry.AbsolutePath, newPath);
        entry.AbsolutePath = newPath;
        entry.RelativePath = Path.GetRelativePath(root, newPath);
        entry.FileNameWithoutExtension = Path.GetFileNameWithoutExtension(newPath);
        entry.IndexedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
    }

    /// <summary>Gleicht einen verschobenen Eintrag mit dem Inhalt am neuen Ort ab.</summary>
    /// <remarks>
    /// Eine reine Umbenennung lässt Größe und Änderungszeit stehen; dann bleibt auch der Hash.
    /// Haben sie sich geändert, wurde zugleich geschrieben (Speichern über eine Umbenennung), und
    /// der Inhalt wird neu gelesen. Größe und Zeit ohne neuen Hash zu übernehmen hieße, dass jeder
    /// spätere Abgleich die Datei für unverändert hält.
    /// </remarks>
    public async Task RefreshMovedAsync(IMarkdownFileRepository repository, MarkdownFile entry, FileStamp stamp, CancellationToken cancellationToken)
    {
        if (entry.SizeBytes == stamp.SizeBytes && entry.LastWriteTimeUtc == stamp.LastWriteTimeUtc)
        {
            repository.Update(entry);
            return;
        }

        string? hash = await ComputeHashWithRetryAsync(entry.AbsolutePath, cancellationToken).ConfigureAwait(false);
        if (hash is null)
        {
            // Nicht lesbar: Pfad übernehmen, Größe und Zeit nicht. Der nächste Abgleich sieht die
            // Abweichung und versucht es erneut.
            repository.Update(entry);
            return;
        }

        _ = ApplyContent(repository, entry, stamp, hash);
    }

    [LoggerMessage(EventId = 105, Level = LogLevel.Debug, Message = "Datei indiziert: {Path}")]
    private static partial void LogFileAdded(ILogger logger, string path);

    [LoggerMessage(EventId = 106, Level = LogLevel.Debug, Message = "Datei aktualisiert: {Path}")]
    private static partial void LogFileUpdated(ILogger logger, string path);

    [LoggerMessage(EventId = 107, Level = LogLevel.Debug, Message = "Datei berührt (Inhalt unverändert): {Path}")]
    private static partial void LogFileTouched(ILogger logger, string path);

    [LoggerMessage(EventId = 108, Level = LogLevel.Debug, Message = "Datei umbenannt: {OldPath} -> {NewPath}")]
    private static partial void LogFileRenamed(ILogger logger, string oldPath, string newPath);

    [LoggerMessage(EventId = 110, Level = LogLevel.Warning, Message = "Hash-Berechnung endgültig fehlgeschlagen für {Path}.")]
    private static partial void LogHashFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 121, Level = LogLevel.Debug, Message = "Datei verschwand während der Verarbeitung: {Path}")]
    private static partial void LogFileVanished(ILogger logger, string path);

    private MarkdownFile Create(string path, string root, FileStamp stamp, string hash) => new()
    {
        Id = Guid.NewGuid(),
        AbsolutePath = path,
        RelativePath = Path.GetRelativePath(root, path),
        FileNameWithoutExtension = Path.GetFileNameWithoutExtension(path),
        SizeBytes = stamp.SizeBytes,
        LastWriteTimeUtc = stamp.LastWriteTimeUtc,
        ContentHash = hash,
        IndexedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
    };

    private IndexChange ApplyContent(IMarkdownFileRepository repository, MarkdownFile entry, FileStamp stamp, string hash)
    {
        entry.SizeBytes = stamp.SizeBytes;
        entry.LastWriteTimeUtc = stamp.LastWriteTimeUtc;
        repository.Update(entry);
        if (entry.ContentHash == hash)
        {
            LogFileTouched(_logger, entry.AbsolutePath);
            return IndexChange.Touched;
        }

        entry.ContentHash = hash;
        entry.IndexedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        LogFileUpdated(_logger, entry.AbsolutePath);
        return IndexChange.Updated;
    }

    private async Task<string?> ComputeHashWithRetryAsync(string path, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < MaxHashAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await _hashCalculator.ComputeAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Gelöscht zwischen Aufzählung und Lesen, etwa eine Zwischendatei des Editors. Ein
                // neuer Versuch fände sie auch nicht; der nächste Abgleich räumt den Eintrag ab.
                if (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    LogFileVanished(_logger, path);
                    return null;
                }
                if (attempt == MaxHashAttempts - 1)
                {
                    LogHashFailed(_logger, path, ex);
                    return null;
                }
                TimeSpan delay = TimeSpan.FromMilliseconds(50L << attempt);
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        return null;
    }
}
