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

        // Original-Kanten ueber den gesamten Source-Snapshot — wird fuer die Status-Anzeige
        // "X von Y dargestellt" gebraucht; entspricht der Kanten-Menge bei IncludeIsolatedNodes=true
        // und ohne jegliche Pfad-Filter.
        List<GraphEdge> allEdges = new(source.Documents.Count);
        foreach (GraphSourceDocument document in source.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string slug in ExtractSlugs(document.OutlinksJson))
            {
                string normalized = _slugNormalizer.ToSlug(slug);
                if (!slugIndex.TryGetValue(normalized, out Guid targetId) || targetId == document.MarkdownFileId)
                {
                    continue;
                }
                allEdges.Add(new GraphEdge(document.MarkdownFileId, targetId));
            }
        }

        int originalNodeCount = source.Files.Count;
        int originalEdgeCount = allEdges.Count;

        HashSet<Guid> retainedIds = SelectRetainedFiles(source.Files, filter);
        List<GraphEdge> filteredEdges = allEdges
            .Where(edge => retainedIds.Contains(edge.SourceId) && retainedIds.Contains(edge.TargetId))
            .ToList();

        Dictionary<Guid, int> incoming = new(retainedIds.Count);
        Dictionary<Guid, int> outgoing = new(retainedIds.Count);
        foreach (Guid id in retainedIds)
        {
            incoming[id] = 0;
            outgoing[id] = 0;
        }
        foreach (GraphEdge edge in filteredEdges)
        {
            incoming[edge.TargetId] += 1;
            outgoing[edge.SourceId] += 1;
        }

        if (!_options.IncludeIsolatedNodes)
        {
            HashSet<Guid> connected = new(retainedIds.Count);
            foreach (Guid id in retainedIds)
            {
                if (incoming[id] + outgoing[id] > 0)
                {
                    _ = connected.Add(id);
                }
            }
            retainedIds = connected;
        }

        List<GraphSourceFile> retainedFiles = source.Files
            .Where(file => retainedIds.Contains(file.Id))
            .ToList();

        if (retainedFiles.Count > _options.MaxNodes)
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
            retainedFiles = retainedFiles.GetRange(0, _options.MaxNodes);
            LogSnapshotTrimmed(_logger, trimmedFrom, _options.MaxNodes);

            HashSet<Guid> trimmedIds = new(retainedFiles.Count);
            foreach (GraphSourceFile file in retainedFiles)
            {
                _ = trimmedIds.Add(file.Id);
            }
            retainedIds = trimmedIds;

            filteredEdges = filteredEdges
                .Where(edge => retainedIds.Contains(edge.SourceId) && retainedIds.Contains(edge.TargetId))
                .ToList();

            incoming.Clear();
            foreach (Guid id in retainedIds)
            {
                incoming[id] = 0;
            }
            foreach (GraphEdge edge in filteredEdges)
            {
                incoming[edge.TargetId] += 1;
            }
        }

        List<GraphNode> nodes = new(retainedFiles.Count);
        foreach (GraphSourceFile file in retainedFiles)
        {
            nodes.Add(new GraphNode(file.Id, file.FileNameWithoutExtension, file.RelativePath, incoming[file.Id]));
        }

        LogSnapshotBuilt(_logger, nodes.Count, filteredEdges.Count);
        return new GraphSnapshot(nodes, filteredEdges, originalNodeCount, originalEdgeCount);
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

    private Dictionary<string, Guid> BuildSlugIndex(IReadOnlyList<GraphSourceFile> files)
    {
        Dictionary<string, Guid> index = new(files.Count, StringComparer.Ordinal);
        foreach (GraphSourceFile file in files)
        {
            string slug = _slugNormalizer.ToSlug(file.FileNameWithoutExtension);
            if (slug.Length == 0)
            {
                continue;
            }
            // Erste-Definition-gewinnt: bewusste Wahl, identisch zur Auflösungs-Konvention
            // in MarkdownFileRepository.FindIdByFileNameAsync (stabile Reihenfolge).
            _ = index.TryAdd(slug, file.Id);
        }
        return index;
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

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "Graph-Snapshot getrimmt — {OriginalCount} Knoten ueberstiegen die Obergrenze, behalten werden die Top {RetainedCount} nach Verbindungsgrad.")]
    private static partial void LogSnapshotTrimmed(ILogger logger, int originalCount, int retainedCount);
}
