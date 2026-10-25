namespace MdExplorer.Graph.Models;

/// <summary>
/// Knoten im WikiLink-Graphen; repräsentiert genau eine Markdown-Datei.
/// <see cref="IncomingLinkCount"/> bestimmt die Knoten-Größe in der Visualisierung;
/// der Radius wächst im JS-Frontend mit <c>log10(IncomingLinkCount + 1)</c>, damit ein viel
/// verlinktes Dokument nicht das ganze Bild bedeckt.
/// </summary>
/// <param name="Id">Stabiler Identifier, entspricht der <c>MarkdownFile.Id</c>.</param>
/// <param name="Title">Anzeigename des Knotens (Dateiname ohne Erweiterung).</param>
/// <param name="RelativePath">Pfad relativ zum Root (Tooltip und Sortierung).</param>
/// <param name="IncomingLinkCount">Eingehende WikiLink-Verbindungen (für Knoten-Größe).</param>
public sealed record GraphNode(
    Guid Id,
    string Title,
    string RelativePath,
    int IncomingLinkCount);
