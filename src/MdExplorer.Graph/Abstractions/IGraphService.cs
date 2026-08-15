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
    /// Pfad-Ausschlüsse und Knoten-Obergrenzen kommen aus den <c>GraphOptions</c>,
    /// der pro-Aufruf <paramref name="filter"/> setzt zusätzlich einen Pfad-Prefix.
    /// </summary>
    Task<GraphSnapshot> BuildSnapshotAsync(GraphFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// Liefert die Verbindungen eines einzelnen Dokuments — in beide Richtungen.
    /// </summary>
    /// <remarks>
    /// Bewusst hier und nicht als eigener Dienst: Die Auflösung eines WikiLink-Ziels auf eine
    /// Datei ist dieselbe wie beim Graphen. Zwei Auflösungen nebeneinander liefen irgendwann
    /// auseinander, und dann zeigte der Graph eine Verbindung, die das Dokument nicht kennt.
    /// Die Ausschlüsse und Obergrenzen des Graphen gelten hier <b>nicht</b>: Sie dienen der
    /// Darstellbarkeit eines Bildes, nicht der Frage, was an einem Dokument hängt.
    /// </remarks>
    Task<DocumentRelations> GetRelationsAsync(Guid markdownFileId, CancellationToken cancellationToken);
}
