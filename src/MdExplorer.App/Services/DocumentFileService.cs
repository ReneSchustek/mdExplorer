using System.IO;
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
/// steht die Datei bereits am neuen Ort — dann ist der Eintrag veraltet, und der nächste
/// Indexer-Lauf richtet ihn wieder. Umgekehrt wäre der Schaden größer: ein Index, der auf
/// einen Pfad zeigt, den niemand angelegt hat.
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
    public Task<DocumentFileResult> RenameAsync(Guid markdownFileId, string newFileName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newFileName);

        return MoveToAsync(
            markdownFileId,
            file => Path.Combine(DirectoryOf(file.AbsolutePath), WithMarkdownExtension(newFileName)),
            "umbenannt",
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<DocumentFileResult> MoveAsync(Guid markdownFileId, string targetDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        return MoveToAsync(
            markdownFileId,
            file => Path.Combine(targetDirectory, Path.GetFileName(file.AbsolutePath)),
            "verschoben",
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DocumentFileResult> DeleteAsync(Guid markdownFileId, CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            IMarkdownFileRepository files = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();
            MarkdownFile? file = await files.GetByIdAsync(markdownFileId, cancellationToken).ConfigureAwait(false);
            if (file is null)
            {
                return DocumentFileResult.Failed("Die Datei steht nicht mehr im Index.");
            }

            try
            {
                _fileSystem.DeleteFile(file.AbsolutePath);
            }
            catch (IOException exception)
            {
                LogOperationFailed(_logger, "gelöscht", file.AbsolutePath, exception);
                return DocumentFileResult.Failed($"Die Datei ließ sich nicht löschen: {exception.Message}");
            }
            catch (UnauthorizedAccessException exception)
            {
                LogOperationFailed(_logger, "gelöscht", file.AbsolutePath, exception);
                return DocumentFileResult.Failed($"Die Datei ließ sich nicht löschen: {exception.Message}");
            }

            files.Remove(file);
            _ = await files.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new DocumentFileResult(true, $"„{file.FileNameWithoutExtension}“ wurde gelöscht.", null);
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

    /// <summary>Bildet den relativen Pfad neu — er hängt am bisherigen Verhältnis zur Wurzel.</summary>
    /// <remarks>
    /// Aus dem bisherigen Paar aus absolutem und relativem Pfad lässt sich die Wurzel
    /// zurückrechnen, ohne die Einstellungen zu befragen. Liegt das Ziel außerhalb dieser
    /// Wurzel, bleibt nur der Dateiname — der nächste Indexer-Lauf setzt den Eintrag dann
    /// unter der Wurzel neu an, unter die das Ziel tatsächlich gehört.
    /// </remarks>
    private static string RelativePathFor(MarkdownFile file, string newAbsolutePath)
    {
        string normalizedOld = file.AbsolutePath.Replace('\\', '/');
        string normalizedRelative = file.RelativePath.Replace('\\', '/');

        if (!normalizedOld.EndsWith(normalizedRelative, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileName(newAbsolutePath);
        }

        string root = normalizedOld[..^normalizedRelative.Length];
        string normalizedNew = newAbsolutePath.Replace('\\', '/');

        return normalizedNew.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? normalizedNew[root.Length..]
            : Path.GetFileName(newAbsolutePath);
    }

    [LoggerMessage(EventId = 1500, Level = LogLevel.Warning, Message = "Datei {Path} konnte nicht {Operation} werden.")]
    private static partial void LogOperationFailed(ILogger logger, string operation, string path, Exception exception);

    private async Task<DocumentFileResult> MoveToAsync(
        Guid markdownFileId,
        Func<MarkdownFile, string> targetPath,
        string operation,
        CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            IMarkdownFileRepository files = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();
            MarkdownFile? file = await files.GetByIdAsync(markdownFileId, cancellationToken).ConfigureAwait(false);
            if (file is null)
            {
                return DocumentFileResult.Failed("Die Datei steht nicht mehr im Index.");
            }

            string destination = targetPath(file);
            if (string.Equals(destination, file.AbsolutePath, StringComparison.OrdinalIgnoreCase))
            {
                return DocumentFileResult.Failed("Ziel und Quelle sind dieselbe Datei.");
            }

            try
            {
                _fileSystem.MoveFile(file.AbsolutePath, destination);
            }
            catch (IOException exception)
            {
                LogOperationFailed(_logger, operation, file.AbsolutePath, exception);
                return DocumentFileResult.Failed($"Die Datei ließ sich nicht {operation}: {exception.Message}");
            }
            catch (UnauthorizedAccessException exception)
            {
                LogOperationFailed(_logger, operation, file.AbsolutePath, exception);
                return DocumentFileResult.Failed($"Die Datei ließ sich nicht {operation}: {exception.Message}");
            }

            file.RelativePath = RelativePathFor(file, destination);
            file.AbsolutePath = destination;
            file.FileNameWithoutExtension = Path.GetFileNameWithoutExtension(destination);
            files.Update(file);
            _ = await files.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new DocumentFileResult(true, $"„{file.FileNameWithoutExtension}“ wurde {operation}.", destination);
        }
    }
}
