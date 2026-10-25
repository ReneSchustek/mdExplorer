using System.IO;
using System.Linq;
using System.Text;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Text;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.Services;
using MdExplorer.TagCloud.Abstractions;
using Microsoft.Extensions.Logging;

namespace MdExplorer.TagCloud.Services;

/// <summary>
/// Projektweite Tag-Verwaltung. Liest betroffene Pfade über <see cref="ITagFileLookupQuery"/>,
/// schreibt jede Datei über <see cref="IFileSystem.WriteAllBytesAtomicAsync"/> atomar neu
/// (Temp + Move) und überlässt den DB-Re-Sync dem Indexer-Watcher. Body-Vorkommen werden
/// per Slug-Identität erkannt (Boundary-Regex + Normalizer), Frontmatter-Einträge
/// werden zeilenbasiert manipuliert, gleicher Datei-Schreibpfad für Rename, Merge und Delete.
/// </summary>
public sealed partial class TagManagementService : ITagManagementService
{
    // Die Vorschau im Dialog zeigt Beispiele, keine vollständige Liste; die Gesamtzahl steht daneben.
    private const int SamplePathLimit = 10;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly ITagFileLookupQuery _lookupQuery;
    private readonly IFileSystem _fileSystem;
    private readonly IMarkdownTagRewriter _rewriter;
    private readonly ILogger<TagManagementService> _logger;

    /// <summary>Erzeugt den Service und bindet die Pflichtabhängigkeiten.</summary>
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
        Dictionary<string, string?> operations = new(StringComparer.Ordinal)
        {
            [oldSlug] = ValidatedName(newRawName, nameof(newRawName)),
        };
        return ApplyAsync(oldSlug, operations, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TagRewriteResult> MergeAsync(string sourceSlug, string targetRawName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRawName);
        Dictionary<string, string?> operations = new(StringComparer.Ordinal)
        {
            [sourceSlug] = ValidatedName(targetRawName, nameof(targetRawName)),
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

    // Geprüft wird vor der ersten Datei und nach derselben Regel, nach der der Index Schlagworte
    // im Text erkennt. Ein Name mit Leerzeichen oder YAML-Zeichen stünde sonst in jeder Datei
    // anders, oder wäre nicht wiederzufinden.
    private static string ValidatedName(string rawName, string parameterName)
    {
        string sanitized = rawName.TrimStart('#').Trim();
        if (!HashtagPattern.IsValidName(sanitized))
        {
            throw new ArgumentException("Kein gültiger Schlagwort-Name.", parameterName);
        }
        return sanitized;
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Tag-Operation '{Slug}' abgeschlossen — {FilesAffected}/{FilesAttempted} Dateien geändert, {ErrorCount} Fehler.")]
    private static partial void LogOperationCompleted(ILogger logger, string slug, int filesAffected, int filesAttempted, int errorCount);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Tag-Operation '{Slug}' übersprungen — keine indizierten Dateien.")]
    private static partial void LogNoFilesAffected(ILogger logger, string slug);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Datei {Path} konnte für Tag-Operation nicht geschrieben werden.")]
    private static partial void LogFileWriteFailed(ILogger logger, Exception exception, string path);

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
            if (await TryRewriteAsync(absolutePath, operations, errors, cancellationToken).ConfigureAwait(false))
            {
                filesAffected++;
            }
        }

        LogOperationCompleted(_logger, operationSlug, filesAffected, rows.Count, errors.Count);
        return new TagRewriteResult(operationSlug, filesAffected, rows.Count, errors);
    }

    // Ein Schreibfehler kostet nur diese Datei; er steht in der Fehlerliste des Ergebnisses.
    private async Task<bool> TryRewriteAsync(
        string absolutePath,
        Dictionary<string, string?> operations,
        Dictionary<string, string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            return await RewriteSingleFileAsync(absolutePath, operations, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            errors[absolutePath] = ex.Message;
            LogFileWriteFailed(_logger, ex, absolutePath);
            return false;
        }
    }

    private async Task<bool> RewriteSingleFileAsync(
        string absolutePath,
        Dictionary<string, string?> operations,
        CancellationToken cancellationToken)
    {
        byte[] bytes = await _fileSystem.ReadAllBytesAsync(absolutePath, cancellationToken).ConfigureAwait(false);

        // Nur gültiges UTF-8 wird umgeschrieben: Aus einer anders kodierten Datei würden beim
        // Zurückschreiben Ersatzzeichen. Eine Byte-Order-Mark bleibt, wie sie war.
        if (!Utf8Decoder.TryDecodeStrict(bytes, out string original))
        {
            throw new InvalidDataException("Datei ist kein gültiges UTF-8 und bleibt unverändert.");
        }
        string rewritten = _rewriter.Apply(original, operations);
        if (string.Equals(rewritten, original, StringComparison.Ordinal))
        {
            return false;
        }
        bool hadBom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        byte[] payload = hadBom ? [.. Encoding.UTF8.Preamble, .. Utf8NoBom.GetBytes(rewritten)] : Utf8NoBom.GetBytes(rewritten);
        await _fileSystem.WriteAllBytesAtomicAsync(absolutePath, payload, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
