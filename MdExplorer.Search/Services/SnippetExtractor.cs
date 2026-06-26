using MdExplorer.Search.Models;

namespace MdExplorer.Search.Services;

/// <summary>
/// Wertet einen <c>snippet()</c>-Output von FTS5 aus und extrahiert die Highlight-Positionen.
/// Erwartet die in <see cref="OpenMarker"/> und <see cref="CloseMarker"/> definierten Marker
/// (FTS5 wird in <c>Fts5SearchService</c> entsprechend konfiguriert).
/// </summary>
public static class SnippetExtractor
{
    /// <summary>Öffnender Highlight-Marker (HTML-<c>&lt;mark&gt;</c>).</summary>
    public const string OpenMarker = "<mark>";

    /// <summary>Schließender Highlight-Marker (HTML-<c>&lt;/mark&gt;</c>).</summary>
    public const string CloseMarker = "</mark>";

    /// <summary>
    /// Liefert die Highlight-Positionen, die das Snippet als Markup enthält.
    /// Markierungen werden im Ergebnis-Index dort gemessen, wo sie für den Aufrufer
    /// auch im Markup-Text liegen — der <c>&lt;mark&gt;</c>-Marker zählt mit.
    /// </summary>
    /// <param name="snippet">FTS5-Snippet inklusive Markup-Marker.</param>
    /// <returns>Liste der erkannten Highlights mit Start-Position und Laenge.</returns>
    public static IReadOnlyList<SearchHighlight> Extract(string snippet)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        if (snippet.Length == 0)
        {
            return [];
        }

        List<SearchHighlight> highlights = [];
        int searchStart = 0;
        while (searchStart < snippet.Length)
        {
            int openIndex = snippet.IndexOf(OpenMarker, searchStart, StringComparison.Ordinal);
            if (openIndex < 0)
            {
                break;
            }
            int contentStart = openIndex + OpenMarker.Length;
            int closeIndex = snippet.IndexOf(CloseMarker, contentStart, StringComparison.Ordinal);
            if (closeIndex < 0)
            {
                break;
            }
            int length = closeIndex - contentStart;
            highlights.Add(new SearchHighlight(openIndex, length));
            searchStart = closeIndex + CloseMarker.Length;
        }
        return highlights;
    }
}
