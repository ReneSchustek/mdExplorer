using System.Globalization;
using System.IO.Compression;
using System.Text;
using Markdig;
using MdExplorer.Core.Abstractions;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.MarkdigExtensions;
using MdExplorer.Parser.Models;
using MarkdigAst = Markdig.Syntax.MarkdownDocument;

namespace MdExplorer.Parser.Services;

/// <summary>
/// Treibt die vollständige Markdown-Verarbeitung: Markdig-AST aufbauen, Frontmatter/Tags/WikiLinks extrahieren,
/// HTML rendern, GZip-komprimieren und alles in <see cref="ParseResult"/> verpacken.
/// </summary>
/// <remarks>
/// HTML im Quelltext wird maskiert und erscheint als Text. Verweisziele und Attribute bereinigt
/// <see cref="MarkdigExtensions.SafeOutputExtension"/>; die Sicherheitsregel der Vorschau ist eine
/// zweite Schicht dahinter, nicht die einzige.
/// </remarks>
public sealed class MarkdigParser : IMarkdownParser
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly string EngineVersionValue = BuildEngineVersion();

    private readonly IFrontmatterExtractor _frontmatterExtractor;
    private readonly ITagExtractor _tagExtractor;
    private readonly IWikiLinkExtractor _wikiLinkExtractor;
    private readonly ITagNormalizer _tagNormalizer;
    private readonly MarkdownPipeline _pipeline;

    /// <summary>Erzeugt den Parser mit allen Extraktoren und baut die Markdig-Pipeline einmalig auf (thread-safe wiederverwendbar).</summary>
    public MarkdigParser(
        IFrontmatterExtractor frontmatterExtractor,
        ITagExtractor tagExtractor,
        IWikiLinkExtractor wikiLinkExtractor,
        ITagNormalizer tagNormalizer)
    {
        ArgumentNullException.ThrowIfNull(frontmatterExtractor);
        ArgumentNullException.ThrowIfNull(tagExtractor);
        ArgumentNullException.ThrowIfNull(wikiLinkExtractor);
        ArgumentNullException.ThrowIfNull(tagNormalizer);

        _frontmatterExtractor = frontmatterExtractor;
        _tagExtractor = tagExtractor;
        _wikiLinkExtractor = wikiLinkExtractor;
        _tagNormalizer = tagNormalizer;

        _pipeline = IndexPipeline.CreateBuilder(tagNormalizer.TryToSlug).Build();
    }

    /// <inheritdoc />
    public string EngineVersion => EngineVersionValue;

    /// <inheritdoc />
    public ParseResult Parse(string markdownText)
    {
        ArgumentNullException.ThrowIfNull(markdownText);

        MarkdigAst ast = Markdown.Parse(markdownText, _pipeline);
        // Ein AST für alle Auszüge und das HTML: Markdig parst jede Datei nur einmal.
        IReadOnlyDictionary<string, string> frontmatter = _frontmatterExtractor.Extract(ast);
        IReadOnlyList<string> bodyTagNames = _tagExtractor.ExtractFromAst(ast);
        IReadOnlyList<string> linkTargets = _wikiLinkExtractor.Extract(ast);

        IReadOnlyList<string> tagNames = MergeTagNames(bodyTagNames, frontmatter);
        (IReadOnlyList<string> uniqueNames, IReadOnlyList<string> uniqueSlugs) = NormalizeUnique(tagNames);
        (_, IReadOnlyList<string> outlinkSlugs) = NormalizeUnique(linkTargets);

        string html = ast.ToHtml(_pipeline);
        ReadOnlyMemory<byte> compressed = Compress(html);

        return new ParseResult(
            Frontmatter: frontmatter,
            Tags: uniqueSlugs,
            TagNames: uniqueNames,
            OutlinkSlugs: outlinkSlugs,
            RenderedHtmlGz: compressed);
    }

    // Beide Fassungen stehen in der Kennung, weil beide das Ergebnis bestimmen: Markdig
    // entscheidet über die Verschachtelungsgrenze, die eigene Kette über Frontmatter,
    // Schlagworte und WikiLinks. Ein Vermerk über einen Fehlschlag bindet dadurch nur so
    // lange, wie die Fassung unverändert bleibt. Nach einem Update läuft der Versuch von
    // selbst wieder an, ohne dass jemand die Datei anfassen muss.
    private static string BuildEngineVersion()
    {
        string markdigVersion = typeof(Markdown).Assembly.GetName().Version?.ToString() ?? "unbekannt";
        string parserVersion = typeof(MarkdigParser).Assembly.GetName().Version?.ToString() ?? "unbekannt";
        return string.Create(CultureInfo.InvariantCulture, $"markdig/{markdigVersion};parser/{parserVersion}");
    }

    private static IReadOnlyList<string> MergeTagNames(
        IReadOnlyList<string> bodyTagNames,
        IReadOnlyDictionary<string, string> frontmatter)
    {
        if (!frontmatter.TryGetValue("tags", out string? rawTags) || string.IsNullOrWhiteSpace(rawTags))
        {
            return bodyTagNames;
        }

        // Die 4 ist nur eine Startkapazität für die Frontmatter-Einträge, keine Grenze.
        List<string> combined = new(bodyTagNames.Count + 4);
        combined.AddRange(bodyTagNames);
        foreach (string entry in rawTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            combined.Add(entry);
        }
        return combined;
    }

    private static ReadOnlyMemory<byte> Compress(string html)
    {
        byte[] bytes = Utf8NoBom.GetBytes(html);
        using MemoryStream output = new();
        using (GZipStream gzip = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }

    private (IReadOnlyList<string> Names, IReadOnlyList<string> Slugs) NormalizeUnique(IReadOnlyList<string> rawNames)
    {
        List<string> names = new(rawNames.Count);
        List<string> slugs = new(rawNames.Count);
        HashSet<string> seenSlugs = new(StringComparer.Ordinal);
        foreach (string raw in rawNames)
        {
            // Ein Name, der keinen Slug hergibt, wird ausgelassen, nicht gemeldet. Er kommt aus
            // fremdem Text; das ist ein Ergebnis, keine Störung.
            if (!_tagNormalizer.TryToSlug(raw, out string slug))
            {
                continue;
            }

            if (seenSlugs.Add(slug))
            {
                names.Add(raw);
                slugs.Add(slug);
            }
        }
        return (names, slugs);
    }
}
