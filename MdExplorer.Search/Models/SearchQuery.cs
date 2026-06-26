namespace MdExplorer.Search.Models;

/// <summary>
/// Beschreibt eine Sucheingabe gegen den FTS5-Index. Die Roh-Eingabe wird vom
/// <see cref="MdExplorer.Search.Abstractions.ISearchQueryBuilder"/> in eine sichere FTS5-MATCH-Form übersetzt.
/// </summary>
/// <param name="Text">Roher User-Input (z. B. <c>"foo bar" tag:projekt -draft</c>).</param>
/// <param name="Take">Maximale Anzahl Treffer. Werte ≤ 0 werden auf den Service-Default begrenzt.</param>
/// <param name="Skip">Anzahl zu überspringender Treffer (Pagination). Werte &lt; 0 werden auf 0 begrenzt.</param>
/// <param name="Mode">Such-Modus — <see cref="SearchMode.Fts5"/> (Default) oder <see cref="SearchMode.Regex"/>.</param>
/// <param name="Similarity">Ähnlichkeits-Stufe für den FTS5-Modus — siehe <see cref="SimilarityMode"/>.</param>
public sealed record SearchQuery(
    string Text,
    int Take = 20,
    int Skip = 0,
    SearchMode Mode = SearchMode.Fts5,
    SimilarityMode Similarity = SimilarityMode.None);
