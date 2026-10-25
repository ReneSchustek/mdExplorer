using MdExplorer.Core.Abstractions;
using MdExplorer.Graph.Models;
using MdExplorer.Graph.Options;
using Microsoft.Extensions.FileSystemGlobbing;

namespace MdExplorer.Graph.Services;

/// <summary>
/// Wählt die Knoten des Snapshots aus: Ausschlüsse und Pfadfilter, isolierte Knoten und die
/// Obergrenze nach Verbindungsgrad.
/// </summary>
internal static class NodeSelection
{
    /// <summary>Behält die Dateien, die weder ausgeschlossen sind noch außerhalb des Pfadfilters liegen.</summary>
    /// <param name="files">Alle Dateien.</param>
    /// <param name="filter">Der Filter des Aufrufs.</param>
    /// <param name="options">Die Ausschlüsse aus der Konfiguration.</param>
    /// <returns>Die Ids der behaltenen Dateien.</returns>
    public static HashSet<Guid> SelectRetainedFiles(IReadOnlyList<GraphSourceFile> files, GraphFilter filter, GraphOptions options)
    {
        Matcher? exclusions = BuildExclusionMatcher(options);
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

    /// <summary>Behält nur Kanten, deren beide Endpunkte in <paramref name="retainedIds"/> liegen.</summary>
    /// <param name="edges">Die Kanten.</param>
    /// <param name="retainedIds">Die behaltenen Knoten.</param>
    /// <returns>Die verbleibenden Kanten.</returns>
    public static List<GraphEdge> FilterEdges(List<GraphEdge> edges, HashSet<Guid> retainedIds) =>
        edges.Where(edge => retainedIds.Contains(edge.SourceId) && retainedIds.Contains(edge.TargetId)).ToList();

    /// <summary>Zählt Eingangs- und Ausgangsgrad je Knoten.</summary>
    /// <param name="retainedIds">Die Knoten; jeder bekommt einen Eintrag, auch ohne Kante.</param>
    /// <param name="edges">Die Kanten zwischen ihnen.</param>
    /// <returns>Eingangs- und Ausgangsgrad.</returns>
    public static (Dictionary<Guid, int> Incoming, Dictionary<Guid, int> Outgoing) CountDegrees(
        HashSet<Guid> retainedIds,
        List<GraphEdge> edges)
    {
        Dictionary<Guid, int> incoming = new(retainedIds.Count);
        Dictionary<Guid, int> outgoing = new(retainedIds.Count);
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
        return (incoming, outgoing);
    }

    /// <summary>Verwirft isolierte Knoten (Grad 0) für den Fall <c>IncludeIsolatedNodes=false</c>.</summary>
    /// <param name="retainedIds">Die Knoten.</param>
    /// <param name="incoming">Eingangsgrad je Knoten.</param>
    /// <param name="outgoing">Ausgangsgrad je Knoten.</param>
    /// <returns>Die verbundenen Knoten.</returns>
    public static HashSet<Guid> SelectConnectedNodes(
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

    /// <summary>
    /// Reduziert die Knotenmenge auf die ersten <paramref name="maxNodes"/> nach Verbindungsgrad
    /// (Tie-Break: relativer Pfad). Gibt die getrimmten Dateien, die entsprechend gefilterten
    /// Kanten und die für die Restmenge neu gezählten Eingangsgrade zurück.
    /// </summary>
    /// <param name="retainedFiles">Die Dateien; die Liste wird dabei sortiert.</param>
    /// <param name="filteredEdges">Die Kanten zwischen ihnen.</param>
    /// <param name="degrees">Eingangs- und Ausgangsgrad je Knoten.</param>
    /// <param name="maxNodes">Die Obergrenze.</param>
    /// <returns>Dateien, Kanten und Eingangsgrade nach dem Kürzen.</returns>
    public static (List<GraphSourceFile> Files, List<GraphEdge> Edges, Dictionary<Guid, int> Incoming) TrimToMaxNodes(
        List<GraphSourceFile> retainedFiles,
        List<GraphEdge> filteredEdges,
        (Dictionary<Guid, int> Incoming, Dictionary<Guid, int> Outgoing) degrees,
        int maxNodes)
    {
        retainedFiles.Sort((left, right) =>
        {
            int leftDegree = degrees.Incoming[left.Id] + degrees.Outgoing[left.Id];
            int rightDegree = degrees.Incoming[right.Id] + degrees.Outgoing[right.Id];
            int byDegree = rightDegree.CompareTo(leftDegree);
            return byDegree != 0
                ? byDegree
                : string.Compare(left.RelativePath, right.RelativePath, StringComparison.OrdinalIgnoreCase);
        });
        List<GraphSourceFile> trimmedFiles = retainedFiles.GetRange(0, maxNodes);
        HashSet<Guid> trimmedIds = [.. trimmedFiles.Select(file => file.Id)];
        List<GraphEdge> trimmedEdges = FilterEdges(filteredEdges, trimmedIds);
        (Dictionary<Guid, int> trimmedIncoming, _) = CountDegrees(trimmedIds, trimmedEdges);
        return (trimmedFiles, trimmedEdges, trimmedIncoming);
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

    private static Matcher? BuildExclusionMatcher(GraphOptions options)
    {
        if (options.PathExclusions.Count == 0)
        {
            return null;
        }
        Matcher matcher = new(StringComparison.OrdinalIgnoreCase);
        bool any = false;
        foreach (string pattern in options.PathExclusions)
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
}
