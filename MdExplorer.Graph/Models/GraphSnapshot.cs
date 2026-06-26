namespace MdExplorer.Graph.Models;

/// <summary>
/// Vollständiger, immutabler Zustand des WikiLink-Graphen zu einem Zeitpunkt.
/// Wird vom <see cref="MdExplorer.Graph.Abstractions.IGraphService"/> erzeugt und
/// vom UI-Frontend gerendert.
/// </summary>
/// <param name="Nodes">Knoten (nach Filter / Trimmung).</param>
/// <param name="Edges">Gerichtete Kanten zwischen den enthaltenen Knoten.</param>
/// <param name="OriginalNodeCount">Anzahl Files in der Quelle, vor Filter und Trimmung —
/// für die Status-Anzeige „X von Y dargestellt".</param>
/// <param name="OriginalEdgeCount">Anzahl der aus den Quell-Dokumenten ableitbaren
/// WikiLink-Kanten vor Filter und Trimmung.</param>
public sealed record GraphSnapshot(
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    int OriginalNodeCount = 0,
    int OriginalEdgeCount = 0)
{
    /// <summary>Leerer Snapshot — verwendet, wenn der Index keine Dokumente enthält.</summary>
    public static GraphSnapshot Empty { get; } = new([], [], 0, 0);
}
