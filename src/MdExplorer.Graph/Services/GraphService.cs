using MdExplorer.Core.Abstractions;
using MdExplorer.Graph.Abstractions;
using MdExplorer.Graph.Models;
using MdExplorer.Graph.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MdExplorer.Graph.Services;

/// <summary>
/// Aggregiert <see cref="IGraphSourceProvider"/>-Rohdaten zu einem <see cref="GraphSnapshot"/>:
/// löst WikiLink-Slugs gegen die normalisierten Dateinamen auf, verwirft Self-Loops und
/// Verweise auf unbekannte Ziele, zählt Verbindungen je Knoten und wendet die Filter aus
/// <see cref="GraphOptions"/> sowie dem pro-Aufruf <see cref="GraphFilter"/> an.
/// </summary>
public sealed partial class GraphService : IGraphService, IWikiLinkResolver
{
    private readonly IGraphSourceProvider _sourceProvider;
    private readonly ITagNormalizer _slugNormalizer;
    private readonly EdgeBuilder _edges;
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
        _edges = new EdgeBuilder(slugNormalizer, logger);
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

        Dictionary<string, Guid> slugIndex = SlugIndex.Build(source.Files, _slugNormalizer);
        List<GraphEdge> allEdges = _edges.Build(source.Documents, slugIndex, cancellationToken);

        HashSet<Guid> retainedIds = NodeSelection.SelectRetainedFiles(source.Files, filter, _options);
        (List<GraphSourceFile> files, List<GraphEdge> edges, Dictionary<Guid, int> incoming) =
            SelectNodes(source.Files, retainedIds, NodeSelection.FilterEdges(allEdges, retainedIds));

        List<GraphNode> nodes = [.. files.Select(file => new GraphNode(file.Id, file.FileNameWithoutExtension, file.RelativePath, incoming[file.Id]))];
        LogSnapshotBuilt(_logger, nodes.Count, edges.Count);
        return new GraphSnapshot(nodes, edges, source.Files.Count, allEdges.Count);
    }

    /// <inheritdoc />
    public async Task<DocumentRelations> GetRelationsAsync(Guid markdownFileId, CancellationToken cancellationToken)
    {
        if (markdownFileId == Guid.Empty)
        {
            return DocumentRelations.Empty;
        }

        // Nur die Nachbarschaft: Die Datei-Stammdaten (drei kurze Spalten) braucht die Auflösung von
        // Name auf Datei; die teuren Verweis-Listen kommen nur für das Dokument und die, die es
        // nennen. Die Auflösung ist dieselbe wie beim vollen Graphen, nur auf kleinerer Menge.
        IReadOnlyList<GraphSourceFile> files = await _sourceProvider
            .LoadFilesAsync(cancellationToken)
            .ConfigureAwait(false);
        Dictionary<Guid, GraphSourceFile> filesById = files.ToDictionary(file => file.Id);
        if (!filesById.TryGetValue(markdownFileId, out GraphSourceFile? own)
            || !_slugNormalizer.TryToSlug(own.FileNameWithoutExtension, out string ownSlug))
        {
            return DocumentRelations.Empty;
        }

        IReadOnlyList<GraphSourceDocument> neighborhood = await _sourceProvider
            .LoadNeighborhoodDocumentsAsync(markdownFileId, ownSlug, cancellationToken)
            .ConfigureAwait(false);
        List<GraphEdge> edges = _edges.Build(neighborhood, SlugIndex.Build(files, _slugNormalizer), cancellationToken);

        return new DocumentRelations(
            RelatedFrom(edges, filesById, markdownFileId, incoming: false),
            RelatedFrom(edges, filesById, markdownFileId, incoming: true));
    }

    /// <inheritdoc />
    public async Task<Guid?> ResolveAsync(string wikiLinkTarget, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wikiLinkTarget);

        // Der Slug eines Slugs ist er selbst; Ziel aus der Vorschau und Name aus dem Text
        // landen damit auf demselben Schlüssel.
        if (!_slugNormalizer.TryToSlug(wikiLinkTarget, out string slug))
        {
            return null;
        }

        IReadOnlyList<GraphSourceFile> files = await _sourceProvider
            .LoadFilesAsync(cancellationToken)
            .ConfigureAwait(false);

        return SlugIndex.Build(files, _slugNormalizer).TryGetValue(slug, out Guid id) ? id : null;
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

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Graph-Snapshot erzeugt — {NodeCount} Knoten, {EdgeCount} Kanten.")]
    private static partial void LogSnapshotBuilt(ILogger logger, int nodeCount, int edgeCount);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "Graph-Snapshot getrimmt — {OriginalCount} Knoten überstiegen die Obergrenze, behalten werden die Top {RetainedCount} nach Verbindungsgrad.")]
    private static partial void LogSnapshotTrimmed(ILogger logger, int originalCount, int retainedCount);

    /// <summary>
    /// Zählt die Grade, verwirft auf Wunsch isolierte Knoten und kürzt auf die Obergrenze.
    /// </summary>
    private (List<GraphSourceFile> Files, List<GraphEdge> Edges, Dictionary<Guid, int> Incoming) SelectNodes(
        IReadOnlyList<GraphSourceFile> allFiles,
        HashSet<Guid> retainedIds,
        List<GraphEdge> edges)
    {
        (Dictionary<Guid, int> Incoming, Dictionary<Guid, int> Outgoing) degrees = NodeSelection.CountDegrees(retainedIds, edges);
        if (!_options.IncludeIsolatedNodes)
        {
            retainedIds = NodeSelection.SelectConnectedNodes(retainedIds, degrees.Incoming, degrees.Outgoing);
        }

        List<GraphSourceFile> files = [.. allFiles.Where(file => retainedIds.Contains(file.Id))];
        if (files.Count <= _options.MaxNodes)
        {
            return (files, edges, degrees.Incoming);
        }

        LogSnapshotTrimmed(_logger, files.Count, _options.MaxNodes);
        return NodeSelection.TrimToMaxNodes(files, edges, degrees, _options.MaxNodes);
    }
}
