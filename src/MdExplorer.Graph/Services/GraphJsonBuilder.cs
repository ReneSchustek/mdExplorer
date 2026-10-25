using System.Text.Json;
using MdExplorer.Graph.Models;

namespace MdExplorer.Graph.Services;

/// <summary>
/// Serialisiert einen <see cref="GraphSnapshot"/> in das vom JS-Frontend
/// erwartete Format: <c>{ "nodes": [...], "edges": [...] }</c> mit
/// camelCase-Property-Namen. Das Format hängt an keiner Grafik-Bibliothek; das Graph-Fenster
/// rechnet sein Layout selbst und liest nur diese Felder.
/// </summary>
public static class GraphJsonBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>Wandelt den Snapshot in eine kompakte JSON-Repräsentation um.</summary>
    /// <param name="snapshot">Graph-Snapshot mit Knoten und Kanten.</param>
    /// <returns>Kompakte camelCase-JSON-Repräsentation für das WebView2-Frontend.</returns>
    public static string Serialize(GraphSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        GraphDto dto = new(
            [.. snapshot.Nodes.Select(node => new GraphNodeDto(node.Id, node.Title, node.RelativePath, node.IncomingLinkCount))],
            [.. snapshot.Edges.Select(edge => new GraphEdgeDto(edge.SourceId, edge.TargetId))]);
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    /// <summary>
    /// Wurzel des JSON-Vertrags mit <c>graph.js</c>. Eigene Typen statt der Modelle, damit eine
    /// Umbenennung im Modell nicht unbemerkt die Feldnamen ändert, die das Skript liest.
    /// </summary>
    private sealed record GraphDto(IReadOnlyList<GraphNodeDto> Nodes, IReadOnlyList<GraphEdgeDto> Edges);

    /// <summary>Ein Knoten, wie <c>graph.js</c> ihn liest.</summary>
    private sealed record GraphNodeDto(Guid Id, string Title, string RelativePath, int IncomingLinkCount);

    /// <summary>Eine Kante, wie <c>graph.js</c> sie liest; Endpunkte ohne Knoten verwirft das Skript.</summary>
    private sealed record GraphEdgeDto(Guid SourceId, Guid TargetId);
}
