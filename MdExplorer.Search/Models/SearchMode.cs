namespace MdExplorer.Search.Models;

/// <summary>
/// Modus, in dem eine <see cref="SearchQuery"/> ausgeführt wird.
/// </summary>
public enum SearchMode
{
    /// <summary>FTS5-Volltextsuche — Default, Stichwort-/Phrasen-/Boolean-Match.</summary>
    Fts5 = 0,

    /// <summary>
    /// RegEx-Modus: FTS5 dient nur als Vorfilter über die im Pattern
    /// vorkommenden Wort-Tokens; <c>System.Text.RegularExpressions.Regex</c> filtert
    /// die Treffer-Bodies nach.
    /// </summary>
    Regex = 1,
}
