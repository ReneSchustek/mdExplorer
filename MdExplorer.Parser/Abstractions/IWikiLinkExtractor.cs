namespace MdExplorer.Parser.Abstractions;

/// <summary>
/// Extrahiert WikiLink-Ziele (<c>[[Ziel]]</c> oder <c>[[Ziel|Anzeigetext]]</c>) aus einem geparsten AST.
/// Liefert immer den Ziel-Teil, niemals den Anzeigetext.
/// </summary>
public interface IWikiLinkExtractor
{
    /// <summary>Liefert die Roh-Ziel-Namen aller WikiLinks im Dokument.</summary>
    IReadOnlyList<string> Extract(Markdig.Syntax.MarkdownDocument ast);
}
