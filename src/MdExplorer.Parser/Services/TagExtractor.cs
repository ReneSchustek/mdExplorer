using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdExplorer.Core.Abstractions;
using MdExplorer.Parser.Abstractions;

namespace MdExplorer.Parser.Services;

/// <summary>
/// Extrahiert Hashtags (<c>#tag</c>) aus dem Markdig-AST. Inline-Code (<see cref="CodeInline"/>),
/// Fenced-/Indented-Code-Blocks und das YAML-Frontmatter werden bewusst übergangen; Tags daraus zählen nicht.
/// </summary>
/// <remarks>
/// Der Settings-Schalter <see cref="Core.Models.IndexingSettings.AutoExtractHashtags"/>
/// steuert die Body-Extraktion. Steht er auf <see langword="false"/>, liefert der Extractor
/// eine leere Liste. Frontmatter-<c>tags</c>-Werte werden weiterhin vom <see cref="MarkdigParser"/>
/// hinzugefügt, weil sie explizit gesetzt sind.
/// </remarks>
public sealed partial class TagExtractor : ITagExtractor
{
    [GeneratedRegex(HashtagPattern.Expression, RegexOptions.CultureInvariant, HashtagPattern.MatchTimeoutMilliseconds)]
    private static partial Regex HashtagRegex();

    // Dieselben Erweiterungen wie der Index, einmal gebaut und von allen Aufrufen geteilt: Die
    // Pipeline ist nach dem Bauen unveränderlich und damit threadsicher. Der Normalisierer bildet
    // nur die Slugs der WikiLink-Ziele, die für das Zählen keine Rolle spielen; er sorgt dafür,
    // dass ein WikiLink hier dieselbe Gestalt hat wie im Index.
    private static readonly MarkdownPipeline EditorPipeline =
        IndexPipeline.CreateBuilder(new TagNormalizer().TryToSlug).Build();

    private readonly ISettingsService _settingsService;

    /// <summary>Erzeugt den Extractor und bindet den Settings-Service für den Auto-Tagging-Schalter.</summary>
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
        // Der Editor zählt mit der Pipeline des Index. Ohne die YAML-Erweiterung läse er einen
        // Frontmatter-Block als gewöhnlichen Text und zeigte eine Raute daraus als Schlagwort an,
        // das der Index nie erfasst.
        MarkdownDocument ast = Markdown.Parse(markdownText, EditorPipeline);
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

    /// <summary>
    /// Liefert die Textstücke, in denen ein Hashtag als Schlagwort zählt, in Dokumentreihenfolge.
    /// </summary>
    /// <remarks>
    /// Die eine Antwort auf die Frage „wo zählt eine Raute?“. Der Umschreiber benutzt sie ebenfalls,
    /// damit er genau das ersetzt, was hier gezählt wird: kein Code, kein Frontmatter, kein
    /// Verweisziel.
    /// </remarks>
    /// <param name="ast">Das mit der Index-Pipeline gelesene Dokument.</param>
    /// <returns>Die Literale außerhalb von Code und Frontmatter.</returns>
    internal static IEnumerable<LiteralInline> EnumerateBodyLiterals(MarkdownDocument ast)
    {
        foreach (Block block in ast)
        {
            // Der Frontmatter-Block ist in Markdig ebenfalls ein CodeBlock und fällt hier mit heraus.
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

    private static List<string> ExtractFromAstCore(MarkdownDocument ast)
    {
        List<string> ordered = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (LiteralInline literal in EnumerateBodyLiterals(ast))
        {
            string text = literal.Content.ToString();
            foreach (Match match in HashtagRegex().Matches(text))
            {
                // Der Name kommt in der zusammengesetzten Form (NFC) in den Index, auch wenn die
                // Datei „ä“ als „a“ mit nachgestellten Punkten speichert: Angezeigt und verglichen
                // wird dann ein Zeichen, nicht zwei.
                string tag = match.Groups["name"].Value.Normalize(NormalizationForm.FormC);
                // Ein Farbwert erfüllt jede Bedingung eines Schlagworts und ist doch keines.
                if (HashtagPattern.IsColorLiteral(tag))
                {
                    continue;
                }
                if (seen.Add(tag))
                {
                    ordered.Add(tag);
                }
            }
        }

        return ordered;
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

    private bool IsAutoExtractEnabled() => _settingsService.Current.Indexing.AutoExtractHashtags;
}
