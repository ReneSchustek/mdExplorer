using System.Text.Json;
using MdExplorer.Core.Abstractions;
using MdExplorer.Graph.Models;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Graph.Services;

/// <summary>
/// Liest die Verweis-Listen der Dokumente und löst sie gegen den <see cref="SlugIndex"/> zu Kanten auf.
/// </summary>
internal sealed partial class EdgeBuilder
{
    private readonly ITagNormalizer _slugNormalizer;
    private readonly ILogger _logger;

    /// <summary>Erzeugt den Baustein mit der Normalisierung und dem Protokoll des Dienstes.</summary>
    public EdgeBuilder(ITagNormalizer slugNormalizer, ILogger logger)
    {
        _slugNormalizer = slugNormalizer;
        _logger = logger;
    }

    /// <summary>
    /// Baut die vollständige Kantenmenge über die übergebenen Dokumente (vor Filtern). Verwirft
    /// Self-Loops und Verweise auf unbekannte Ziele. Je Dokument gibt es höchstens eine Kante zu
    /// jedem Ziel: Zwei Schreibweisen wie <c>[[Projekt X]]</c> und <c>[[projekt x]]</c> führen
    /// auf dieselbe Datei, und eine zweite Kante zählte sie doppelt in den Eingangsgrad.
    /// </summary>
    /// <param name="documents">Die Dokumente mit ihren Verweis-Listen.</param>
    /// <param name="slugIndex">Slug auf Datei-Id.</param>
    /// <param name="cancellationToken">Abbruch.</param>
    /// <returns>Die Kanten.</returns>
    public List<GraphEdge> Build(
        IReadOnlyCollection<GraphSourceDocument> documents,
        Dictionary<string, Guid> slugIndex,
        CancellationToken cancellationToken)
    {
        List<GraphEdge> allEdges = new(documents.Count);
        foreach (GraphSourceDocument document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HashSet<Guid> linkedTargets = [];
            foreach (string slug in ExtractSlugs(document))
            {
                // TryToSlug statt ToSlug: ein WikiLink-Ziel aus reinen Sonderzeichen (z. B. [[+]])
                // liefert keinen Slug. Überspringen statt den ganzen Graph-Build abstürzen zu lassen.
                if (!_slugNormalizer.TryToSlug(slug, out string normalized))
                {
                    continue;
                }
                if (!slugIndex.TryGetValue(normalized, out Guid targetId)
                    || targetId == document.MarkdownFileId
                    || !linkedTargets.Add(targetId))
                {
                    continue;
                }
                allEdges.Add(new GraphEdge(document.MarkdownFileId, targetId));
            }
        }
        return allEdges;
    }

    [LoggerMessage(EventId = 1102, Level = LogLevel.Warning, Message = "Verweis-Liste von Dokument {MarkdownFileId} ist kein gültiges JSON; seine Kanten fehlen bis zum nächsten Parsen.")]
    private static partial void LogBrokenOutlinks(ILogger logger, Guid markdownFileId, Exception exception);

    private static List<string> SlugsOf(JsonElement root)
    {
        List<string> slugs = [];
        if (root.ValueKind != JsonValueKind.Array)
        {
            return slugs;
        }
        foreach (JsonElement element in root.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                string? value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    slugs.Add(value);
                }
            }
        }
        return slugs;
    }

    // Die Liste schreibt der Parser; kaputt ist sie nur nach einem Fehler beim Schreiben oder
    // einem Eingriff von außen. Das Dokument verliert dann seine Kanten, aber nicht still.
    private List<string> ExtractSlugs(GraphSourceDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.OutlinksJson))
        {
            return [];
        }
        try
        {
            using JsonDocument parsed = JsonDocument.Parse(document.OutlinksJson);
            return SlugsOf(parsed.RootElement);
        }
        catch (JsonException ex)
        {
            LogBrokenOutlinks(_logger, document.MarkdownFileId, ex);
            return [];
        }
    }
}
