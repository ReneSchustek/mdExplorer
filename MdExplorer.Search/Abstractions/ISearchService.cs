using MdExplorer.Search.Models;

namespace MdExplorer.Search.Abstractions;

/// <summary>
/// Volltextsuche über alle indizierten Markdown-Inhalte (FTS5).
/// Implementierungen müssen FTS5-Injection-frei sein — User-Input wird ausschließlich über
/// <see cref="ISearchQueryBuilder"/> in eine sichere MATCH-Expression überführt.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Führt eine Suche aus und liefert eine BM25-sortierte Ergebnisliste.
    /// Liefert eine leere Liste bei leerer Eingabe.
    /// </summary>
    /// <param name="query">Suchanfrage inklusive Pagination-Parameter.</param>
    /// <param name="cancellationToken">Abbruchsteuerung.</param>
    Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken);
}
