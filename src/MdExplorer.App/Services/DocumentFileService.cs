using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using MdExplorer.App.Localization;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Graph.Abstractions;
using MdExplorer.Graph.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Services;

/// <summary>
/// Führt die Datei-Vorgänge eines Dokuments aus und zieht den Index nach.
/// </summary>
/// <remarks>
/// Die Reihenfolge ist bewusst gewählt: erst die Datei, dann der Index. Scheitert die
/// Datei-Operation, bleibt der Index unberührt und stimmt weiter. Scheitert der Index-Schritt,
/// steht die Datei bereits am neuen Ort; der Vorgang gilt dann als gelungen, die Meldung sagt
/// dazu, dass der nächste Abgleich des Indexers den Eintrag nachzieht. Umgekehrt wäre der
/// Schaden größer: ein Index, der auf einen Pfad zeigt, den niemand angelegt hat.
/// </remarks>
internal sealed partial class DocumentFileService : IDocumentFileService
{
    private const string MarkdownExtension = ".md";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<DocumentFileService> _logger;

    /// <summary>Erzeugt den Dienst.</summary>
    public DocumentFileService(
        IServiceScopeFactory scopeFactory,
        IFileSystem fileSystem,
        ILogger<DocumentFileService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>Die drei Vorgänge; jeder hat eigene Meldungen, weil das Verb im Satz nicht einsetzbar ist.</summary>
    private enum FileOperation
    {
        Rename,
        Move,
        Delete,
    }

    /// <inheritdoc />
    public async Task<DocumentImpact> GetImpactAsync(Guid markdownFileId, CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            IMarkdownFileRepository files = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();
            MarkdownFile? file = await files.GetByIdAsync(markdownFileId, cancellationToken).ConfigureAwait(false);
            if (file is null)
            {
                return DocumentImpact.Unknown;
            }

            IGraphService graph = scope.ServiceProvider.GetRequiredService<IGraphService>();
            DocumentRelations relations = await graph.GetRelationsAsync(markdownFileId, cancellationToken).ConfigureAwait(false);

            return new DocumentImpact(file.FileNameWithoutExtension, relations.Incoming.Count);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Der Name kommt aus einem Eingabefeld. Das ViewModel prüft ihn schon; hier steht die
    /// Prüfung trotzdem, weil sie die Grenze des Dateisystems schützt und nicht die Bedienung.
    /// </remarks>
    public Task<DocumentFileResult> RenameAsync(Guid markdownFileId, string newFileName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newFileName);

        if (!FileNameRule.IsPlainName(newFileName))
        {
            return Task.FromResult(DocumentFileResult.Failed(UiText.Get("DocumentFile_InvalidName")));
        }

        return MoveToAsync(
            markdownFileId,
            file => Path.Combine(DirectoryOf(file.AbsolutePath), WithMarkdownExtension(newFileName)),
            FileOperation.Rename,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<DocumentFileResult> MoveAsync(Guid markdownFileId, string targetDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        if (!Path.IsPathFullyQualified(targetDirectory))
        {
            return Task.FromResult(DocumentFileResult.Failed(UiText.Get("DocumentFile_InvalidTarget")));
        }

        // GetFullPath löst „..“-Teile auf, damit der Vergleich mit der Quelle und der relative
        // Pfad im Index mit dem Ort rechnen, an dem die Datei tatsächlich landet.
        string directory = Path.GetFullPath(targetDirectory);
        return MoveToAsync(
            markdownFileId,
            file => Path.Combine(directory, Path.GetFileName(file.AbsolutePath)),
            FileOperation.Move,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DocumentFileResult> DeleteAsync(Guid markdownFileId, CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            IMarkdownFileRepository files = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();
            Lookup lookup = await FindAsync(files, markdownFileId, cancellationToken).ConfigureAwait(false);
            if (!lookup.Found)
            {
                return lookup.Failure;
            }

            MarkdownFile file = lookup.File;

            DocumentFileResult? failure = TryOnDisk(() => _fileSystem.DeleteFile(file.AbsolutePath), FileOperation.Delete, file.AbsolutePath);
            if (failure is not null)
            {
                return failure;
            }

            files.Remove(file);
            return await CommitAsync(files, FileOperation.Delete, file.FileNameWithoutExtension, newAbsolutePath: null, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Hängt die Erweiterung an, wenn der Nutzer sie nicht mitgetippt hat.</summary>
    private static string WithMarkdownExtension(string fileName)
    {
        string trimmed = fileName.Trim();

        return trimmed.EndsWith(MarkdownExtension, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + MarkdownExtension;
    }

    private static string DirectoryOf(string absolutePath) => Path.GetDirectoryName(absolutePath) ?? string.Empty;

    /// <summary>Bildet den relativen Pfad neu, denn er hängt am bisherigen Verhältnis zur Wurzel.</summary>
    /// <remarks>
    /// Liegt das Ziel außerhalb der Wurzel des Eintrags, bleibt nur der Dateiname; der nächste
    /// Indexer-Lauf setzt den Eintrag dann unter der Wurzel neu an, unter die das Ziel
    /// tatsächlich gehört.
    /// </remarks>
    private static string RelativePathFor(MarkdownFile file, string newAbsolutePath)
    {
        string? root = IndexRootPath.Of(file.AbsolutePath, file.RelativePath);
        string normalizedNew = newAbsolutePath.Replace('\\', '/');

        return root is not null && normalizedNew.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? normalizedNew[root.Length..]
            : Path.GetFileName(newAbsolutePath);
    }

    private static string DoneMessage(FileOperation operation, string title) => operation switch
    {
        FileOperation.Rename => UiText.Format("DocumentFile_Renamed", title),
        FileOperation.Move => UiText.Format("DocumentFile_Moved", title),
        _ => UiText.Format("DocumentFile_Deleted", title),
    };

    private static string FailedMessage(FileOperation operation, string cause) => operation switch
    {
        FileOperation.Rename => UiText.Format("DocumentFile_RenameFailed", cause),
        FileOperation.Move => UiText.Format("DocumentFile_MoveFailed", cause),
        _ => UiText.Format("DocumentFile_DeleteFailed", cause),
    };

    [LoggerMessage(EventId = 1500, Level = LogLevel.Warning, Message = "Dateivorgang {Operation} an {Path} gescheitert.")]
    private static partial void LogOperationFailed(ILogger logger, FileOperation operation, string path, Exception exception);

    [LoggerMessage(EventId = 1501, Level = LogLevel.Warning, Message = "Dateivorgang {Operation} ausgeführt, Index nicht gespeichert; der nächste Abgleich zieht {Title} nach.")]
    private static partial void LogIndexLagging(ILogger logger, FileOperation operation, string title, Exception exception);

    [LoggerMessage(EventId = 1502, Level = LogLevel.Warning, Message = "Index für den Dateivorgang an {MarkdownFileId} nicht lesbar.")]
    private static partial void LogLookupFailed(ILogger logger, Guid markdownFileId, Exception exception);

    private async Task<DocumentFileResult> MoveToAsync(
        Guid markdownFileId,
        Func<MarkdownFile, string> targetPath,
        FileOperation operation,
        CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            IMarkdownFileRepository files = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();
            Lookup lookup = await FindAsync(files, markdownFileId, cancellationToken).ConfigureAwait(false);
            if (!lookup.Found)
            {
                return lookup.Failure;
            }

            MarkdownFile file = lookup.File;

            // Ordinal: Eine reine Änderung der Groß-/Kleinschreibung ist eine Umbenennung, die
            // Windows ausführt, und keine „dieselbe Datei“.
            string destination = targetPath(file);
            if (string.Equals(destination, file.AbsolutePath, StringComparison.Ordinal))
            {
                return DocumentFileResult.Failed(UiText.Get("DocumentFile_SameFile"));
            }

            return await RelocateAsync(files, file, destination, operation, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Verschiebt die Datei und schreibt den neuen Ort in den Eintrag.</summary>
    private async Task<DocumentFileResult> RelocateAsync(
        IMarkdownFileRepository files,
        MarkdownFile file,
        string destination,
        FileOperation operation,
        CancellationToken cancellationToken)
    {
        DocumentFileResult? failure = TryOnDisk(() => _fileSystem.MoveFile(file.AbsolutePath, destination), operation, file.AbsolutePath);
        if (failure is not null)
        {
            return failure;
        }

        file.RelativePath = RelativePathFor(file, destination);
        file.AbsolutePath = destination;
        file.FileNameWithoutExtension = Path.GetFileNameWithoutExtension(destination);
        files.Update(file);
        return await CommitAsync(files, operation, file.FileNameWithoutExtension, destination, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Liest den Eintrag; fehlt er oder ist der Index nicht lesbar, liegt statt seiner das Ergebnis vor.</summary>
    private async Task<Lookup> FindAsync(
        IMarkdownFileRepository files,
        Guid markdownFileId,
        CancellationToken cancellationToken)
    {
        try
        {
            MarkdownFile? file = await files.GetByIdAsync(markdownFileId, cancellationToken).ConfigureAwait(false);
            return file is null
                ? new Lookup(null, DocumentFileResult.Failed(UiText.Get("DocumentFile_NotInIndex")))
                : new Lookup(file, null);
        }
        catch (DbException exception)
        {
            LogLookupFailed(_logger, markdownFileId, exception);
            return new Lookup(null, DocumentFileResult.Failed(UiText.Get("DocumentFile_IndexUnavailable")));
        }
    }

    /// <summary>Führt den Schritt auf der Platte aus; scheitert er, liegt das Ergebnis vor, sonst <see langword="null"/>.</summary>
    private DocumentFileResult? TryOnDisk(Action step, FileOperation operation, string path)
    {
        try
        {
            step();
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogOperationFailed(_logger, operation, path, exception);
            return DocumentFileResult.Failed(FailedMessage(operation, exception.Message));
        }
    }

    /// <summary>Speichert den Index-Schritt; scheitert er, gilt der Vorgang trotzdem, mit Hinweis.</summary>
    private async Task<DocumentFileResult> CommitAsync(
        IMarkdownFileRepository files,
        FileOperation operation,
        string title,
        string? newAbsolutePath,
        CancellationToken cancellationToken)
    {
        string done = DoneMessage(operation, title);
        try
        {
            _ = await files.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new DocumentFileResult(true, done, newAbsolutePath);
        }
        catch (DbException exception)
        {
            LogIndexLagging(_logger, operation, title, exception);
            return new DocumentFileResult(true, UiText.Format("DocumentFile_IndexLagging", done), newAbsolutePath);
        }
    }

    /// <summary>Der gefundene Eintrag oder das Ergebnis, mit dem der Vorgang endet.</summary>
    private sealed record Lookup(MarkdownFile? File, DocumentFileResult? Failure)
    {
        [MemberNotNullWhen(true, nameof(File))]
        [MemberNotNullWhen(false, nameof(Failure))]
        public bool Found => File is not null;
    }
}
