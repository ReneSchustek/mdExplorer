using System.Linq;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.MarkdigExtensions;

namespace MdExplorer.Parser.Services;

/// <summary>
/// Liest <see cref="WikiLinkInline"/>-Knoten aus dem Markdig-AST und liefert die Roh-Zielnamen.
/// Duplikate werden in der Reihenfolge des ersten Auftretens deduziert.
/// </summary>
public sealed class WikiLinkExtractor : IWikiLinkExtractor
{
    /// <inheritdoc />
    public IReadOnlyList<string> Extract(MarkdownDocument ast)
    {
        ArgumentNullException.ThrowIfNull(ast);

        HashSet<string> seen = new(StringComparer.Ordinal);
        return ast.Descendants<WikiLinkInline>()
            .Where(link => seen.Add(link.Target))
            .Select(link => link.Target)
            .ToList();
    }
}
