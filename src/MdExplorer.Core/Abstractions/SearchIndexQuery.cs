namespace MdExplorer.Core.Abstractions;

/// <summary>FTS5-Suchanfrage mit Pfad-Filter, Pagination und Gewichten.</summary>
/// <param name="MatchExpression">FTS5-MATCH-Ausdruck (vom <c>SearchQueryBuilder</c> aufbereitet).</param>
/// <param name="PathLikePattern">Optionaler LIKE-Pfadfilter (z. B. <c>"notes%"</c>) mit Backslash als Escape-Zeichen für <c>%</c> und <c>_</c>, oder <see langword="null"/>.</param>
/// <param name="Take">Maximale Trefferzahl.</param>
/// <param name="Skip">Anzahl zu überspringender Treffer (Pagination).</param>
/// <param name="TitleWeight">bm25-Gewicht der Titelspalte.</param>
/// <param name="BodyWeight">bm25-Gewicht der Body-Spalte.</param>
/// <param name="TagsWeight">bm25-Gewicht der Tags-Spalte.</param>
/// <param name="FrontmatterWeight">bm25-Gewicht der Frontmatter-Spalte.</param>
/// <param name="SnippetTokenCount">Anzahl Tokens für die FTS5-Snippet-Erzeugung.</param>
public sealed record SearchIndexQuery(
    string MatchExpression,
    string? PathLikePattern,
    int Take,
    int Skip,
    double TitleWeight,
    double BodyWeight,
    double TagsWeight,
    double FrontmatterWeight,
    int SnippetTokenCount);
