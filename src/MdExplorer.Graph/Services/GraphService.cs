using System.Linq;
using System.Text.Json;
using MdExplorer.Core.Abstractions;
using MdExplorer.Graph.Abstractions;
using MdExplorer.Graph.Models;
using MdExplorer.Graph.Options;
using MdExplorer.Parser.Abstractions;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MdExplorer.Graph.Services;

/// <summary>
/// Aggregiert <see cref="IGraphSourceProvider"/>-Rohdaten zu einem <see cref="GraphSnapshot"/>:
/// löst WikiLink-Slugs gegen die normalisierten Dateinamen auf, verwirft Self-Loops und
/// Verweise auf unbekannte Ziele, zählt Verbindungen je Knoten und wendet die Filter aus
/// <see cref="GraphOptions"/> sowie dem pro-Aufruf <see cref="GraphFilter"/> an.
/// </summary>
public sealed partial class GraphService : IGraphService
{
    private readonly IGraphSourceProvider _sourceProvider;
    private readonly ITagNormalizer _slugNormalizer;
    private readonly GraphOptions _options;
    private readonly ILogger<GraphService> _logger;

    /// <summary>Erzeugt den Service und löst Pflichtabhängigkeiten auf.</summary>
    public GraphService(
        IGraphSourceProvider sourceProvider,
        ITagNormalizer slugNormalizer,
        IOptions<GraphOptions> options,
        ILogger<GraphService> logger)
    {
        ArgumentNullException.ThrowIfNull(sourceProvider);
        ArgumentNullException.ThrowIfNull(slugNormalizer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _sourceProvider = sourceProvider;
        _slugNormalizer = slugNormalizer;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GraphSnapshot> BuildSnapshotAsync(GraphFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        GraphSourceData source = await _sourceProvider.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (source.Files.Count == 0)
        {
            return GraphSnapshot.Empty;
        }

        Dictionary<string, Guid> slugIndex = BuildSlugIndex(source.Files);
        List<GraphEdge> allEdges = BuildAllEdges(source.Documents, slugIndex, cancellationToken);

        int originalNodeCount = source.Files.Count;
        int originalEdgeCount = allEdges.Count;

        HashSet<Guid> retainedIds = SelectRetainedFiles(source.Files, filter);
        List<GraphEdge> filteredEdges = FilterEdges(allEdges, retainedIds);

        Dictionary<Guid, int> incoming = new(retainedIds.Count);
        Dictionary<Guid, int> outgoing = new(retainedIds.Count);
        CountDegrees(retainedIds, filteredEdges, incoming, outgoing);

        if (!_options.IncludeIsolatedNodes)
        {
            retainedIds = SelectConnectedNodes(retainedIds, incoming, outgoing);
        }

        List<GraphSourceFile> retainedFiles = source.Files
            .Where(file => retainedIds.Contains(file.Id))
            .ToList();

        if (retainedFiles.Count > _options.MaxNodes)
        {
            (retainedFiles, filteredEdges, incoming) = TrimToMaxNodes(retainedFiles, filteredEdges, incoming, outgoing);
        }

        List<GraphNode> nodes = new(retainedFiles.Count);
        foreach (GraphSourceFile file in retainedFiles)
        {
            nodes.Add(new GraphNode(file.Id, file.FileNameWithoutExtension, file.RelativePath, incoming[file.Id]));
        }

        LogSnapshotBuilt(_logger, nodes.Count, filteredEdges.Count);
        return new GraphSnapshot(nodes, filteredEdges, originalNodeCount, originalEdgeCount);
    }

    /// <inheritdoc />
    public async Task<DocumentRelations> GetRelationsAsync(Guid markdownFileId, CancellationToken cancellationToken)
    {
        if (markdownFileId == Guid.Empty)
        {
            return DocumentRelations.Empty;
        }

        GraphSourceData source = await _sourceProvider.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (source.Files.Count == 0)
        {
            return DocumentRelations.Empty;
        }

        Dictionary<string, Guid> slugIndex = BuildSlugIndex(source.Files);
        List<GraphEdge> edges = BuildAllEdges(source.Documents, slugIndex, cancellationToken);
        Dictionary<Guid, GraphSourceFile> filesById = source.Files.ToDictionary(file => file.Id);

        List<RelatedDocument> outgoing = RelatedFrom(edges, filesById, markdownFileId, incoming: false);
        List<RelatedDocument> incoming = RelatedFrom(edges, filesById, markdownFileId, incoming: true);

        return new DocumentRelations(outgoing, incoming);
    }

    /// <summary>
    /// Sammelt die Gegenstellen eines Knotens in einer Richtung.
    /// </summary>
    /// <remarks>
    /// Doppelte Verweise auf dasselbe Ziel zählen einmal: Wer eine Datei dreimal im Text
    /// erwähnt, hat sie einmal verknüpft. Sortiert nach Pfad, damit die Reihenfolge nicht von
    /// der Reihenfolge im Text abhängt.
    /// </remarks>
    private static List<RelatedDocument> RelatedFrom(
        List<GraphEdge> edges,
        Dictionary<Guid, GraphSourceFile> filesById,
        Guid markdownFileId,
        bool incoming)
    {
        IEnumerable<Guid> partners = edges
            .Where(edge => (incoming ? edge.TargetId : edge.SourceId) == markdownFileId)
            .Select(edge => incoming ? edge.SourceId : edge.TargetId)
            .Distinct();

        return
        [
            .. partners
                .Where(filesById.ContainsKey)
                .Select(id => filesById[id])
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(file => new RelatedDocument(file.Id, file.FileNameWithoutExtension, file.RelativePath))
        ];
    }

    /// <summary>Behält nur Kanten, deren beide Endpunkte in <paramref name="retainedIds"/> liegen.</summary>
    private static List<GraphEdge> FilterEdges(List<GraphEdge> edges, HashSet<Guid> retainedIds) =>
        edges.Where(edge => retainedIds.Contains(edge.SourceId) && retainedIds.Contains(edge.TargetId)).ToList();

    /// <summary>Zählt ein-/ausgehenden Grad je Knoten in die vorbelegten Zähl-Maps.</summary>
    private static void CountDegrees(
        HashSet<Guid> retainedIds,
        List<GraphEdge> edges,
        Dictionary<Guid, int> incoming,
        Dictionary<Guid, int> outgoing)
    {
        foreach (Guid id in retainedIds)
        {
            incoming[id] = 0;
            outgoing[id] = 0;
        }
        foreach (GraphEdge edge in edges)
        {
            incoming[edge.TargetId] += 1;
            outgoing[edge.SourceId] += 1;
        }
    }

    /// <summary>Verwirft isolierte Knoten (Grad 0) für den Fall <c>IncludeIsolatedNodes=false</c>.</summary>
    private static HashSet<Guid> SelectConnectedNodes(
        HashSet<Guid> retainedIds,
        Dictionary<Guid, int> incoming,
        Dictionary<Guid, int> outgoing)
    {
        HashSet<Guid> connected = new(retainedIds.Count);
        foreach (Guid id in retainedIds)
        {
            if (incoming[id] + outgoing[id] > 0)
            {
                _ = connected.Add(id);
            }
        }
        return connected;
    }

    private static string NormalizeRelativePath(string relativePath) =>
        string.IsNullOrEmpty(relativePath) ? string.Empty : relativePath.Replace('\\', '/');

    private static string? NormalizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return null;
        }
        return prefix.Replace('\\', '/').TrimStart('/');
    }

    private static IEnumerable<string> ExtractSlugs(string outlinksJson)
    {
        if (string.IsNullOrWhiteSpace(outlinksJson))
        {
            yield break;
        }
        JsonDocument? parsed;
        try
        {
            parsed = JsonDocument.Parse(outlinksJson);
        }
        catch (JsonException)
        {
            yield break;
        }
        using (parsed)
        {
            if (parsed.RootElement.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }
            foreach (JsonElement element in parsed.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    string? value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        yield return value;
                    }
                }
            }
        }
    }

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Graph-Snapshot erzeugt — {NodeCount} Knoten, {EdgeCount} Kanten.")]
    private static partial void LogSnapshotBuilt(ILogger logger, int nodeCount, int edgeCount);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "Graph-Snapshot getrimmt — {OriginalCount} Knoten überstiegen die Obergrenze, behalten werden die Top {RetainedCount} nach Verbindungsgrad.")]
    private static partial void LogSnapshotTrimmed(ILogger logger, int originalCount, int retainedCount);

    /// <summary>
    /// Baut die vollständige Kantenmenge über den gesamten Source-Snapshot (vor Filtern) — löst
    /// WikiLink-Slugs gegen den Index auf, verwirft Self-Loops und Verweise auf unbekannte Ziele.
    /// Dient auch der Status-Anzeige „X von Y dargestellt".
    /// </summary>
    private List<GraphEdge> BuildAllEdges(
        IReadOnlyCollection<GraphSourceDocument> documents,
        Dictionary<string, Guid> slugIndex,
        CancellationToken cancellationToken)
    {
        List<GraphEdge> allEdges = new(documents.Count);
        foreach (GraphSourceDocument document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string slug in ExtractSlugs(document.OutlinksJson))
            {
                // TryToSlug statt ToSlug: ein WikiLink-Ziel aus reinen Sonderzeichen (z. B. [[+]])
                // liefert keinen Slug — überspringen statt den ganzen Graph-Build abstürzen zu lassen.
                if (!_slugNormalizer.TryToSlug(slug, out string normalized))
                {
                    continue;
                }
                if (!slugIndex.TryGetValue(normalized, out Guid targetId) || targetId == document.MarkdownFileId)
                {
                    continue;
                }
                allEdges.Add(new GraphEdge(document.MarkdownFileId, targetId));
            }
        }
        return allEdges;
    }

    /// <summary>
    /// Reduziert die Knotenmenge auf die Top-<c>MaxNodes</c> nach Verbindungsgrad (Tie-Break:
    /// relativer Pfad). Gibt die getrimmten Dateien, die entsprechend gefilterten Kanten und die
    /// für die Restmenge neu gezählten Eingangsgrade zurück.
    /// </summary>
    private (List<GraphSourceFile> Files, List<GraphEdge> Edges, Dictionary<Guid, int> Incoming) TrimToMaxNodes(
        List<GraphSourceFile> retainedFiles,
        List<GraphEdge> filteredEdges,
        Dictionary<Guid, int> incoming,
        Dictionary<Guid, int> outgoing)
    {
        retainedFiles.Sort((left, right) =>
        {
            int leftDegree = incoming[left.Id] + outgoing[left.Id];
            int rightDegree = incoming[right.Id] + outgoing[right.Id];
            int byDegree = rightDegree.CompareTo(leftDegree);
            return byDegree != 0
                ? byDegree
                : string.Compare(left.RelativePath, right.RelativePath, StringComparison.OrdinalIgnoreCase);
        });
        int trimmedFrom = retainedFiles.Count;
        List<GraphSourceFile> trimmedFiles = retainedFiles.GetRange(0, _options.MaxNodes);
        LogSnapshotTrimmed(_logger, trimmedFrom, _options.MaxNodes);

        HashSet<Guid> trimmedIds = new(trimmedFiles.Count);
        foreach (GraphSourceFile file in trimmedFiles)
        {
            _ = trimmedIds.Add(file.Id);
        }

        List<GraphEdge> trimmedEdges = FilterEdges(filteredEdges, trimmedIds);

        Dictionary<Guid, int> trimmedIncoming = new(trimmedIds.Count);
        foreach (Guid id in trimmedIds)
        {
            trimmedIncoming[id] = 0;
        }
        foreach (GraphEdge edge in trimmedEdges)
        {
            trimmedIncoming[edge.TargetId] += 1;
        }

        return (trimmedFiles, trimmedEdges, trimmedIncoming);
    }

    private HashSet<Guid> SelectRetainedFiles(IReadOnlyList<GraphSourceFile> files, GraphFilter filter)
    {
        Matcher? exclusions = BuildExclusionMatcher();
        string? prefix = NormalizePrefix(filter.PathPrefix);

        HashSet<Guid> retained = new(files.Count);
        foreach (GraphSourceFile file in files)
        {
            string relative = NormalizeRelativePath(file.RelativePath);
            if (exclusions is not null && exclusions.Match(relative).HasMatches)
            {
                continue;
            }
            if (prefix is not null && !relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            _ = retained.Add(file.Id);
        }
        return retained;
    }

    private Matcher? BuildExclusionMatcher()
    {
        if (_options.PathExclusions.Count == 0)
        {
            return null;
        }
        Matcher matcher = new(StringComparison.OrdinalIgnoreCase);
        bool any = false;
        foreach (string pattern in _options.PathExclusions)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }
            _ = matcher.AddInclude(pattern);
            any = true;
        }
        return any ? matcher : null;
    }

    private Dictionary<string, Guid> BuildSlugIndex(IReadOnlyList<GraphSourceFile> files)
    {
        Dictionary<string, Guid> index = new(files.Count, StringComparer.Ordinal);
        foreach (GraphSourceFile file in files)
        {
            // TryToSlug: ein Dateiname aus reinen Sonderzeichen (z. B. "#.md" — unter Windows gültig)
            // liefert keinen Slug — überspringen statt eine ArgumentException nach oben zu werfen.
            if (!_slugNormalizer.TryToSlug(file.FileNameWithoutExtension, out string slug))
            {
                continue;
            }
            // Erste-Definition-gewinnt: bewusste Wahl, identisch zur Auflösungs-Konvention
            // in MarkdownFileRepository.FindIdByFileNameAsync (stabile Reihenfolge).
            _ = index.TryAdd(slug, file.Id);
        }
        return index;
    }
}
