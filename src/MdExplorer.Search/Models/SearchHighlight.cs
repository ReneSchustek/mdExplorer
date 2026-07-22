namespace MdExplorer.Search.Models;

/// <summary>
/// Position eines Treffer-Highlights innerhalb des <see cref="SearchResult.Snippet"/>.
/// Bezieht sich auf die <c>&lt;mark&gt;</c>-Marker des FTS5-<c>snippet()</c>-Outputs.
/// </summary>
/// <param name="Start">Null-basierter Zeichen-Offset des Treffer-Beginns innerhalb des Snippets.</param>
/// <param name="Length">Länge des Treffers in Zeichen.</param>
public sealed record SearchHighlight(int Start, int Length);
