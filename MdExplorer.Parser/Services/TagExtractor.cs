using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdExplorer.Core.Abstractions;
using MdExplorer.Parser.Abstractions;

namespace MdExplorer.Parser.Services;

/// <summary>
/// Extrahiert Hashtags (<c>#tag</c>) aus dem Markdig-AST. Inline-Code (<see cref="CodeInline"/>),
/// Fenced-/Indented-Code-Blocks und das YAML-Frontmatter werden bewusst übergangen — Tags daraus zählen nicht.
/// </summary>
/// <remarks>
/// Der Settings-Schalter <see cref="Core.Models.IndexingSettings.AutoExtractHashtags"/>
/// steuert die Body-Extraktion. Steht er auf <see langword="false"/>, liefert der Extractor
/// eine leere Liste — Frontmatter-<c>tags</c>-Werte werden weiterhin vom <see cref="MarkdigParser"/>
/// hinzugefuegt, weil sie explizit gesetzt sind.
/// </remarks>
public sealed partial class TagExtractor : ITagExtractor
{
    [GeneratedRegex(@"(?<![\w#])#([A-Za-zÄÖÜäöüß][A-Za-z0-9ÄÖÜäöüß_\-]{1,})", RegexOptions.CultureInvariant)]
    private static partial Regex HashtagRegex();

    private readonly ISettingsService _settingsService;

    /// <summary>Erzeugt den Extractor und bindet den Settings-Service fuer den Auto-Tagging-Schalter.</summary>
    public TagExtractor(ISettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        _settingsService = settingsService;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ExtractFromText(string markdownText)
    {
        ArgumentNullException.ThrowIfNull(markdownText);
        if (markdownText.Length == 0)
        {
            return [];
        }
        if (!IsAutoExtractEnabled())
        {
            return [];
        }
        // Minimal-Pipeline: kein WikiLink-Render noetig, kein YAML-Frontmatter — ExtractFromAst ueberspringt
        // CodeBlocks ohnehin. Reduziert Allokationen pro Tastendruck.
        MarkdownDocument ast = Markdown.Parse(markdownText);
        return ExtractFromAstCore(ast);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ExtractFromAst(MarkdownDocument ast)
    {
        ArgumentNullException.ThrowIfNull(ast);
        if (!IsAutoExtractEnabled())
        {
            return [];
        }
        return ExtractFromAstCore(ast);
    }

    private static List<string> ExtractFromAstCore(MarkdownDocument ast)
    {
        List<string> ordered = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (LiteralInline literal in EnumerateBodyLiterals(ast))
        {
            string text = literal.Content.ToString();
            foreach (Match match in HashtagRegex().Matches(text))
            {
                string tag = match.Groups[1].Value;
                if (seen.Add(tag))
                {
                    ordered.Add(tag);
                }
            }
        }

        return ordered;
    }

    private bool IsAutoExtractEnabled() => _settingsService.Current.Indexing.AutoExtractHashtags;

    private static IEnumerable<LiteralInline> EnumerateBodyLiterals(MarkdownDocument ast)
    {
        foreach (Block block in ast)
        {
            if (block is CodeBlock)
            {
                continue;
            }
            foreach (LiteralInline literal in EnumerateLiteralsInBlock(block))
            {
                yield return literal;
            }
        }
    }

    private static IEnumerable<LiteralInline> EnumerateLiteralsInBlock(Block block)
    {
        if (block is LeafBlock leaf && leaf.Inline is { } inline)
        {
            foreach (LiteralInline literal in EnumerateLiteralsInInline(inline))
            {
                yield return literal;
            }
            yield break;
        }
        if (block is ContainerBlock container)
        {
            foreach (Block child in container)
            {
                if (child is CodeBlock)
                {
                    continue;
                }
                foreach (LiteralInline literal in EnumerateLiteralsInBlock(child))
                {
                    yield return literal;
                }
            }
        }
    }

    private static IEnumerable<LiteralInline> EnumerateLiteralsInInline(Inline inline)
    {
        switch (inline)
        {
            case CodeInline:
                yield break;
            case LiteralInline literal:
                yield return literal;
                yield break;
            case ContainerInline container:
                foreach (Inline child in container)
                {
                    foreach (LiteralInline literal in EnumerateLiteralsInInline(child))
                    {
                        yield return literal;
                    }
                }
                yield break;
        }
    }
}
