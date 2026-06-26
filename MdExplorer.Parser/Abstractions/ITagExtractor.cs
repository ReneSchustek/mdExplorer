namespace MdExplorer.Parser.Abstractions;

/// <summary>
/// Extrahiert Hashtag-Tokens (<c>#foo</c>) aus dem Body — explizit ohne Code-Blöcke und Inline-Code.
/// </summary>
public interface ITagExtractor
{
    /// <summary>Liefert die Original-Tag-Namen (ohne führendes <c>#</c>, in Originalschreibweise).</summary>
    IReadOnlyList<string> ExtractFromAst(Markdig.Syntax.MarkdownDocument ast);

    /// <summary>
    /// Liefert die Tag-Namen direkt aus rohem Markdown-Text. Gedacht fuer den Editor:
    /// vermeidet den HTML-Render-Schritt aus <c>IMarkdownParser.Parse</c>, der bei jedem
    /// Tastendruck wiederholt wuerde.
    /// </summary>
    IReadOnlyList<string> ExtractFromText(string markdownText);
}
