using MdExplorer.Parser.Models;

namespace MdExplorer.Parser.Abstractions;

/// <summary>
/// Wandelt Markdown-Quelltext in ein vollständiges <see cref="ParseResult"/> um.
/// Implementierung muss XSS-sicher sein (HTML im Quelltext wird verworfen).
/// </summary>
public interface IMarkdownParser
{
    /// <summary>Parst den Markdown-Text und liefert Frontmatter, Tags, Outlinks und gerendertes HTML.</summary>
    /// <param name="markdownText">Roher Markdown-Inhalt.</param>
    /// <returns>Vollständiges Parse-Ergebnis.</returns>
    ParseResult Parse(string markdownText);
}
