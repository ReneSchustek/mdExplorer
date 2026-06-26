namespace MdExplorer.Parser.Abstractions;

/// <summary>
/// Extrahiert YAML-Frontmatter aus einem geparsten Markdig-AST.
/// </summary>
public interface IFrontmatterExtractor
{
    /// <summary>
    /// Liefert die Frontmatter-Schlüssel-Wert-Paare. Bei Liste werden Werte kommagetrennt zusammengefasst.
    /// Fehlt das Frontmatter, ist das Ergebnis leer.
    /// </summary>
    IReadOnlyDictionary<string, string> Extract(Markdig.Syntax.MarkdownDocument ast);
}
