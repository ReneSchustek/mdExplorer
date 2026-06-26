using System.IO.Compression;
using System.Text;
using Markdig;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.MarkdigExtensions;
using MdExplorer.Parser.Models;
using MarkdigAst = Markdig.Syntax.MarkdownDocument;

namespace MdExplorer.Parser.Services;

/// <summary>
/// Treibt die vollständige Markdown-Verarbeitung: Markdig-AST aufbauen, Frontmatter/Tags/WikiLinks extrahieren,
/// HTML rendern (HTML-Roh-Inputs deaktiviert — XSS-sicher), GZip-komprimieren und alles in <see cref="ParseResult"/> verpacken.
/// </summary>
public sealed class MarkdigParser : IMarkdownParser
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

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

        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseEmphasisExtras()
            .UseYamlFrontMatter()
            .DisableHtml()
            .UseMdExplorerWikiLinks(tagNormalizer.ToSlug)
            .Build();
    }

    /// <inheritdoc />
    public ParseResult Parse(string markdownText)
    {
        ArgumentNullException.ThrowIfNull(markdownText);

        MarkdigAst ast = Markdown.Parse(markdownText, _pipeline);
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

    private static IReadOnlyList<string> MergeTagNames(
        IReadOnlyList<string> bodyTagNames,
        IReadOnlyDictionary<string, string> frontmatter)
    {
        if (!frontmatter.TryGetValue("tags", out string? rawTags) || string.IsNullOrWhiteSpace(rawTags))
        {
            return bodyTagNames;
        }

        List<string> combined = new(bodyTagNames.Count + 4);
        combined.AddRange(bodyTagNames);
        foreach (string entry in rawTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            combined.Add(entry);
        }
        return combined;
    }

    private (IReadOnlyList<string> Names, IReadOnlyList<string> Slugs) NormalizeUnique(IReadOnlyList<string> rawNames)
    {
        List<string> names = new(rawNames.Count);
        List<string> slugs = new(rawNames.Count);
        HashSet<string> seenSlugs = new(StringComparer.Ordinal);
        foreach (string raw in rawNames)
        {
            string slug;
            try
            {
                slug = _tagNormalizer.ToSlug(raw);
            }
            catch (ArgumentException)
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
}
