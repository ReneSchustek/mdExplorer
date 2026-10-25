using MdExplorer.Parser.Models;

namespace MdExplorer.Parser.Abstractions;

/// <summary>
/// Wandelt Markdown-Quelltext in ein vollständiges <see cref="ParseResult"/> um.
/// Das gelieferte HTML darf nichts Ausführbares enthalten: HTML im Quelltext wird maskiert,
/// Verweise führen nur auf relative Ziele, <c>http</c>, <c>https</c>, <c>mailto</c> und
/// <c>mdexplorer</c>, Ereignis- und Stilattribute fallen weg.
/// </summary>
public interface IMarkdownParser
{
    /// <summary>
    /// Kennung der Fassung, mit der dieser Parser arbeitet. Ändert sie sich, kann derselbe
    /// Inhalt ein anderes Ergebnis liefern, und ein zuvor gescheiterter Versuch wird deshalb
    /// wiederholt.
    /// </summary>
    string EngineVersion { get; }

    /// <summary>Parst den Markdown-Text und liefert Frontmatter, Tags, Outlinks und gerendertes HTML.</summary>
    /// <param name="markdownText">Roher Markdown-Inhalt.</param>
    /// <returns>Vollständiges Parse-Ergebnis.</returns>
    ParseResult Parse(string markdownText);
}
