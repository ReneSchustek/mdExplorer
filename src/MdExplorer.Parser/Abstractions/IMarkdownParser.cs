using MdExplorer.Parser.Models;

namespace MdExplorer.Parser.Abstractions;

/// <summary>
/// Wandelt Markdown-Quelltext in ein vollständiges <see cref="ParseResult"/> um.
/// Implementierung muss XSS-sicher sein (HTML im Quelltext wird verworfen).
/// </summary>
public interface IMarkdownParser
{
    /// <summary>
    /// Kennung der Fassung, mit der dieser Parser arbeitet. Ändert sie sich, kann derselbe
    /// Inhalt ein anderes Ergebnis liefern — ein zuvor gescheiterter Versuch wird deshalb
    /// wiederholt.
    /// </summary>
    string EngineVersion { get; }

    /// <summary>Parst den Markdown-Text und liefert Frontmatter, Tags, Outlinks und gerendertes HTML.</summary>
    /// <param name="markdownText">Roher Markdown-Inhalt.</param>
    /// <returns>Vollständiges Parse-Ergebnis.</returns>
    ParseResult Parse(string markdownText);
}
