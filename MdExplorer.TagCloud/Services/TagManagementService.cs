using System.IO;
using System.Linq;
using System.Text;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Text;
using MdExplorer.Parser.Abstractions;
using MdExplorer.TagCloud.Abstractions;
using Microsoft.Extensions.Logging;

namespace MdExplorer.TagCloud.Services;

/// <summary>
/// Projektweite Tag-Verwaltung. Liest betroffene Pfade ueber <see cref="ITagFileLookupQuery"/>,
/// schreibt jede Datei ueber <see cref="IFileSystem.WriteAllBytesAtomicAsync"/> atomar neu
/// (Temp + Move) und ueberlasst den DB-Re-Sync dem Indexer-Watcher. Body-Vorkommen werden
/// per Slug-Identitaet erkannt (Boundary-Regex + Normalizer), Frontmatter-Eintraege
/// werden zeilenbasiert manipuliert — gleicher Datei-Schreibpfad fuer Rename, Merge und Delete.
/// </summary>
public sealed partial class TagManagementService : ITagManagementService
{
    private const int SamplePathLimit = 10;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly ITagFileLookupQuery _lookupQuery;
    private readonly IFileSystem _fileSystem;
    private readonly IMarkdownTagRewriter _rewriter;
    private readonly ILogger<TagManagementService> _logger;

    /// <summary>Erzeugt den Service und bindet die Pflichtabhaengigkeiten.</summary>
    public TagManagementService(
        ITagFileLookupQuery lookupQuery,
        IFileSystem fileSystem,
        IMarkdownTagRewriter rewriter,
        ILogger<TagManagementService> logger)
    {
        ArgumentNullException.ThrowIfNull(lookupQuery);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(rewriter);
        ArgumentNullException.ThrowIfNull(logger);

        _lookupQuery = lookupQuery;
        _fileSystem = fileSystem;
        _rewriter = rewriter;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TagPreview> GetPreviewAsync(string slug, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        IReadOnlyList<TagFileLookupRow> rows = await _lookupQuery
            .GetFilesByTagSlugAsync(slug, cancellationToken).ConfigureAwait(false);
        List<string> samples = new(Math.Min(rows.Count, SamplePathLimit));
        for (int index = 0; index < rows.Count && samples.Count < SamplePathLimit; index++)
        {
            samples.Add(rows[index].RelativePath);
        }
        return new TagPreview(slug, rows.Count, samples);
    }

    /// <inheritdoc />
    public Task<TagRewriteResult> RenameAsync(string oldSlug, string newRawName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(newRawName);
        string sanitized = newRawName.TrimStart('#').Trim();
        if (sanitized.Length == 0)
        {
            throw new ArgumentException("Neuer Tag-Name ist leer.", nameof(newRawName));
        }
        Dictionary<string, string?> operations = new(StringComparer.Ordinal)
        {
            [oldSlug] = sanitized,
        };
        return ApplyAsync(oldSlug, operations, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TagRewriteResult> MergeAsync(string sourceSlug, string targetRawName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRawName);
        string sanitized = targetRawName.TrimStart('#').Trim();
        if (sanitized.Length == 0)
        {
            throw new ArgumentException("Ziel-Tag-Name ist leer.", nameof(targetRawName));
        }
        Dictionary<string, string?> operations = new(StringComparer.Ordinal)
        {
            [sourceSlug] = sanitized,
        };
        return ApplyAsync(sourceSlug, operations, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TagRewriteResult> DeleteAsync(string slug, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        Dictionary<string, string?> operations = new(StringComparer.Ordinal)
        {
            [slug] = null,
        };
        return ApplyAsync(slug, operations, cancellationToken);
    }

    private async Task<TagRewriteResult> ApplyAsync(
        string operationSlug,
        Dictionary<string, string?> operations,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TagFileLookupRow> rows = await _lookupQuery
            .GetFilesByTagSlugAsync(operationSlug, cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            LogNoFilesAffected(_logger, operationSlug);
            return new TagRewriteResult(operationSlug, 0, 0, new Dictionary<string, string>(0, StringComparer.Ordinal));
        }

        Dictionary<string, string> errors = new(StringComparer.Ordinal);
        int filesAffected = 0;
        foreach (string absolutePath in rows.Select(row => row.AbsolutePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                bool changed = await RewriteSingleFileAsync(absolutePath, operations, cancellationToken).ConfigureAwait(false);
                if (changed)
                {
                    filesAffected++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors[absolutePath] = ex.Message;
                LogFileWriteFailed(_logger, ex, absolutePath);
            }
        }

        LogOperationCompleted(_logger, operationSlug, filesAffected, rows.Count, errors.Count);
        return new TagRewriteResult(operationSlug, filesAffected, rows.Count, errors);
    }

    private async Task<bool> RewriteSingleFileAsync(
        string absolutePath,
        Dictionary<string, string?> operations,
        CancellationToken cancellationToken)
    {
        byte[] bytes = await _fileSystem.ReadAllBytesAsync(absolutePath, cancellationToken).ConfigureAwait(false);
        string original = Utf8Decoder.DecodeNoBom(bytes);
        string rewritten = _rewriter.Apply(original, operations);
        if (string.Equals(rewritten, original, StringComparison.Ordinal))
        {
            return false;
        }
        byte[] payload = Utf8NoBom.GetBytes(rewritten);
        await _fileSystem.WriteAllBytesAtomicAsync(absolutePath, payload, cancellationToken).ConfigureAwait(false);
        return true;
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Tag-Operation '{Slug}' abgeschlossen — {FilesAffected}/{FilesAttempted} Dateien geaendert, {ErrorCount} Fehler.")]
    private static partial void LogOperationCompleted(ILogger logger, string slug, int filesAffected, int filesAttempted, int errorCount);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Tag-Operation '{Slug}' uebersprungen — keine indizierten Dateien.")]
    private static partial void LogNoFilesAffected(ILogger logger, string slug);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Datei {Path} konnte fuer Tag-Operation nicht geschrieben werden.")]
    private static partial void LogFileWriteFailed(ILogger logger, Exception exception, string path);
}
