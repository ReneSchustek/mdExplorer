using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Parser.Models;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Parser.Services;

/// <summary>
/// Schreibt die Ergebnisse eines Stapels: Dokumente, Schlagwort-Verknüpfungen und Fehlschlag-Vermerke.
/// </summary>
internal sealed partial class ParsedBatchWriter
{
    // Umlaute bleiben lesbar, die HTML-Sonderzeichen <, > und & werden maskiert. Das JSON landet
    // heute nur in der Datenbank und wird dort wieder als JSON gelesen; bettete es eine spätere
    // Anzeige in HTML ein, stünde Text aus fremden Notizen sonst roh darin.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        WriteIndented = false,
    };

    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    /// <summary>Erzeugt den Schreiber mit Uhr und Protokoll des Orchestrators.</summary>
    public ParsedBatchWriter(TimeProvider timeProvider, ILogger logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Legt Dokumente und Schlagwort-Verknüpfungen des Stapels an beziehungsweise aktualisiert sie.</summary>
    public async Task PersistParsedAsync(
        IMarkdownDocumentRepository docRepo,
        ITagRepository tagRepo,
        List<ParsedEntry> results,
        CancellationToken cancellationToken)
    {
        // Existierende Dokumente einmal je Batch laden statt pro Datei per Point-Lookup, denn die
        // Ziel-Ids stehen zum Batch-Start bereits fest.
        Guid[] resultIds = [.. results.Select(entry => entry.Snapshot.Id)];
        IReadOnlyDictionary<Guid, MarkdownDocument> existingByFileId =
            await docRepo.GetByMarkdownFileIdsAsync(resultIds, cancellationToken).ConfigureAwait(false);

        // Ein Schlagwort-Nachschlag für den ganzen Stapel. Fragte jede Datei einzeln, legten zwei
        // Dateien mit demselben neuen Slug ihn zweimal an, und SaveChanges scheiterte mit
        // SqliteException 19 (UNIQUE constraint failed: Tags.Slug).
        Dictionary<string, Guid> slugToId = await EnsureTagsForBatchAsync(tagRepo, results, cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, IReadOnlyCollection<Guid>> tagIdsByFile = new(results.Count);
        foreach (ParsedEntry entry in results)
        {
            await PersistDocumentAsync(docRepo, existingByFileId, entry, cancellationToken).ConfigureAwait(false);
            tagIdsByFile[entry.Snapshot.Id] = [.. entry.Result.Tags.Select(slug => slugToId[slug])];
        }

        // Die Zuordnungen des ganzen Stapels auf einmal; einzeln je Datei kostete jede eine Abfrage.
        await tagRepo.ReplaceFileTagsAsync(tagIdsByFile, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Schreibt die Fehlschläge dieses Stapels ins Protokoll und in die Datenbank und hebt die
    /// Vermerke der Dateien auf, die wieder parsbar sind. Liefert, ob sich am Bestand der
    /// Vermerke etwas geändert hat.
    /// </summary>
    public async Task<bool> SyncFailureMarksAsync(
        IParseFailureRepository failureRepo,
        ParseCollector collected,
        IReadOnlyDictionary<Guid, ParseFailure> knownFailures,
        string engineVersion,
        CancellationToken cancellationToken)
    {
        Guid[] recovered = [.. collected.Parsed.Select(entry => entry.Snapshot.Id).Where(knownFailures.ContainsKey)];
        await failureRepo.RemoveAsync(recovered, cancellationToken).ConfigureAwait(false);

        foreach (ParseFailureEntry entry in collected.Failed)
        {
            LogFailure(entry, knownFailures);
            await failureRepo.RecordAsync(
                new ParseFailure
                {
                    Id = Guid.NewGuid(),
                    MarkdownFileId = entry.Snapshot.Id,
                    ContentHash = entry.Snapshot.ContentHash,
                    EngineVersion = engineVersion,
                    FailureReason = entry.Reason,
                    FailedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
                },
                cancellationToken).ConfigureAwait(false);
        }

        return recovered.Length > 0 || collected.Failed.Count > 0;
    }

    // Sammelt alle einzigartigen Tag-Slugs aus dem Batch, fragt vorhandene in einem
    // einzigen Roundtrip ab und legt fehlende genau einmal an. Liefert einen vollständigen
    // Slug→Id-Lookup, über den die per-File-Verlinkung deterministisch arbeitet.
    private static async Task<Dictionary<string, Guid>> EnsureTagsForBatchAsync(
        ITagRepository tagRepo,
        List<ParsedEntry> results,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> namesBySlug = NamesBySlug(results);
        Dictionary<string, Guid> slugToId = new(StringComparer.Ordinal);
        if (namesBySlug.Count == 0)
        {
            return slugToId;
        }
        foreach (Tag existing in await tagRepo.GetBySlugsAsync(namesBySlug.Keys, cancellationToken).ConfigureAwait(false))
        {
            slugToId[existing.Slug] = existing.Id;
        }
        foreach (KeyValuePair<string, string> pair in namesBySlug.Where(pair => !slugToId.ContainsKey(pair.Key)))
        {
            Tag created = new() { Id = Guid.NewGuid(), Name = pair.Value, Slug = pair.Key };
            await tagRepo.AddAsync(created, cancellationToken).ConfigureAwait(false);
            slugToId[pair.Key] = created.Id;
        }
        return slugToId;
    }

    // Der erste Originalname je Slug gewinnt; er wird zum Anzeigenamen eines neuen Schlagworts.
    private static Dictionary<string, string> NamesBySlug(List<ParsedEntry> results)
    {
        Dictionary<string, string> namesBySlug = new(StringComparer.Ordinal);
        foreach (ParseResult result in results.Select(entry => entry.Result))
        {
            for (int i = 0; i < result.Tags.Count; i++)
            {
                _ = namesBySlug.TryAdd(result.Tags[i], result.TagNames[i]);
            }
        }
        return namesBySlug;
    }

    private static void Fill(MarkdownDocument document, ParsedEntry entry, DateTime now)
    {
        document.SourceContentHash = entry.Snapshot.ContentHash;
        document.FrontmatterJson = JsonSerializer.Serialize(entry.Result.Frontmatter, JsonOptions);
        document.OutlinksJson = JsonSerializer.Serialize(entry.Result.OutlinkSlugs, JsonOptions);
        document.ParsedAtUtc = now;
        document.SetRenderedHtmlGz(entry.Result.RenderedHtmlGz.Span);
    }

    [LoggerMessage(EventId = 203, Level = LogLevel.Debug, Message = "Dokument hinzugefügt: {Path}")]
    private static partial void LogDocumentAdded(ILogger logger, string path);

    [LoggerMessage(EventId = 204, Level = LogLevel.Debug, Message = "Dokument aktualisiert: {Path}")]
    private static partial void LogDocumentUpdated(ILogger logger, string path);

    [LoggerMessage(EventId = 206, Level = LogLevel.Warning, Message = "Datei konnte nicht geparst werden: {Path}")]
    private static partial void LogParseFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 210, Level = LogLevel.Warning, Message = "Datei nicht parsbar: {Path} — {Reason}")]
    private static partial void LogParseFailedAgain(ILogger logger, string path, string reason);

    private async Task PersistDocumentAsync(
        IMarkdownDocumentRepository docRepo,
        IReadOnlyDictionary<Guid, MarkdownDocument> existingByFileId,
        ParsedEntry entry,
        CancellationToken cancellationToken)
    {
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        if (existingByFileId.TryGetValue(entry.Snapshot.Id, out MarkdownDocument? document))
        {
            Fill(document, entry, now);
            docRepo.Update(document);
            LogDocumentUpdated(_logger, entry.Snapshot.AbsolutePath);
            return;
        }

        document = new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            MarkdownFileId = entry.Snapshot.Id,
            SourceContentHash = entry.Snapshot.ContentHash,
            FrontmatterJson = string.Empty,
            OutlinksJson = string.Empty,
        };
        Fill(document, entry, now);
        await docRepo.AddAsync(document, cancellationToken).ConfigureAwait(false);
        LogDocumentAdded(_logger, entry.Snapshot.AbsolutePath);
    }

    /// <summary>
    /// Der volle Aufrufstapel geht genau einmal je Datei und Inhalt ins Protokoll. Der Renderer
    /// arbeitet rekursiv: Ein einziger Stapel dieser Sorte ist rund 265 Zeilen lang, und
    /// wiederholt geschrieben verdrängt er jede echte Diagnose aus der Datei.
    /// </summary>
    private void LogFailure(ParseFailureEntry entry, IReadOnlyDictionary<Guid, ParseFailure> knownFailures)
    {
        bool sameContentAlreadyLogged =
            knownFailures.TryGetValue(entry.Snapshot.Id, out ParseFailure? previous)
            && string.Equals(previous.ContentHash, entry.Snapshot.ContentHash, StringComparison.Ordinal);

        if (sameContentAlreadyLogged || entry.Failure is null)
        {
            LogParseFailedAgain(_logger, entry.Snapshot.AbsolutePath, entry.Reason);
            return;
        }
        LogParseFailed(_logger, entry.Snapshot.AbsolutePath, entry.Failure);
    }
}
