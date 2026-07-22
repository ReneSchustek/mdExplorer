namespace MdExplorer.Graph.Models;

/// <summary>
/// Gerichtete Kante zwischen zwei <see cref="GraphNode"/>-Instanzen.
/// Quelle ist das Dokument, das den WikiLink enthält; Ziel ist das verlinkte Dokument.
/// </summary>
/// <param name="SourceId">Quell-Knoten (Dokument, das den WikiLink ausspricht).</param>
/// <param name="TargetId">Ziel-Knoten (Dokument, auf das verwiesen wird).</param>
public sealed record GraphEdge(Guid SourceId, Guid TargetId);
