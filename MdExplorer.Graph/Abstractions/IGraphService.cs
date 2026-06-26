using MdExplorer.Graph.Models;

namespace MdExplorer.Graph.Abstractions;

/// <summary>
/// Liefert den aktuellen WikiLink-Graphen über alle indizierten Dokumente.
/// </summary>
public interface IGraphService
{
    /// <summary>
    /// Baut einen frischen <see cref="GraphSnapshot"/>. WikiLinks, deren Ziel
    /// nicht im Index existiert, werden verworfen; Self-Loops ebenfalls. Statische
    /// Pfad-Ausschluesse und Knoten-Obergrenzen kommen aus den <c>GraphOptions</c>,
    /// der pro-Aufruf <paramref name="filter"/> setzt zusaetzlich einen Pfad-Prefix.
    /// </summary>
    Task<GraphSnapshot> BuildSnapshotAsync(GraphFilter filter, CancellationToken cancellationToken);
}
