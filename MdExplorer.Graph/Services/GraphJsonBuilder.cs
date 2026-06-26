using System.Text.Json;
using MdExplorer.Graph.Models;

namespace MdExplorer.Graph.Services;

/// <summary>
/// Serialisiert einen <see cref="GraphSnapshot"/> in das vom JS-Frontend
/// erwartete Format: <c>{ "nodes": [...], "edges": [...] }</c> mit
/// camelCase-Property-Namen. Es wird absichtlich kein d3-spezifisches
/// Format verwendet — das Frontend kann jede vanilla-Force-Implementierung
/// damit füttern.
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
    /// <returns>Kompakte camelCase-JSON-Repraesentation fuer das WebView2-Frontend.</returns>
    public static string Serialize(GraphSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        GraphDto dto = new(
            [.. snapshot.Nodes.Select(node => new GraphNodeDto(node.Id, node.Title, node.RelativePath, node.IncomingLinkCount))],
            [.. snapshot.Edges.Select(edge => new GraphEdgeDto(edge.SourceId, edge.TargetId))]);
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    private sealed record GraphDto(IReadOnlyList<GraphNodeDto> Nodes, IReadOnlyList<GraphEdgeDto> Edges);

    private sealed record GraphNodeDto(Guid Id, string Title, string RelativePath, int IncomingLinkCount);

    private sealed record GraphEdgeDto(Guid SourceId, Guid TargetId);
}
