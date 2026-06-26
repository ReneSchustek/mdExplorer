using System.Diagnostics;
using System.Text;
using MdExplorer.Parser.Models;
using MdExplorer.Parser.Services;
using MdExplorer.Parser.Tests.Helpers;
using MdExplorer.Core.Models;

namespace MdExplorer.Parser.Tests.Services;

public sealed class MarkdigParserTests
{
    private readonly MarkdigParser _sut;

    public MarkdigParserTests()
    {
        TagNormalizer normalizer = new();
        _sut = new MarkdigParser(
            new FrontmatterExtractor(),
            new TagExtractor(new FakeSettingsService()),
            new WikiLinkExtractor(),
            normalizer);
    }

    [Fact]
    public void Parse_OnNull_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => _sut.Parse(null!));
    }

    [Fact]
    public void Parse_OnEmptyString_ReturnsEmptyResult()
    {
        ParseResult result = _sut.Parse(string.Empty);

        Assert.Empty(result.Frontmatter);
        Assert.Empty(result.Tags);
        Assert.Empty(result.TagNames);
        Assert.Empty(result.OutlinkSlugs);
        Assert.Equal(string.Empty, GzipHelper.Decompress(result.RenderedHtmlGz));
    }

    [Fact]
    public void Parse_MergesFrontmatterTagsWithBodyTags()
    {
        const string Markdown = """
            ---
            tags: [foo, bar]
            ---
            Body mit #baz Tag.
            """;

        ParseResult result = _sut.Parse(Markdown);

        Assert.Equal(["baz", "foo", "bar"], result.Tags);
        Assert.Equal(["baz", "foo", "bar"], result.TagNames);
    }

    [Fact]
    public void Parse_DeduplicatesTagsCaseInsensitivelyViaSlug()
    {
        const string Markdown = """
            ---
            tags: [Foo, FOO]
            ---
            #foo und #Foo
            """;

        ParseResult result = _sut.Parse(Markdown);

        Assert.Equal(["foo"], result.Tags);
    }

    [Fact]
    public void Parse_OnXssPayload_EscapesInOutput()
    {
        string html = GzipHelper.Decompress(_sut.Parse("<script>alert(1)</script>").RenderedHtmlGz);

        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_OnInlineHtmlAttribute_DoesNotEmitExecutableTag()
    {
        string html = GzipHelper.Decompress(_sut.Parse("<img src=x onerror=alert(1)>").RenderedHtmlGz);

        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;img", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_OnWikiLink_RewritesToMdExplorerScheme()
    {
        string html = GzipHelper.Decompress(_sut.Parse("Siehe [[Anderes Dokument]].").RenderedHtmlGz);

        Assert.Contains("href=\"mdexplorer://anderes-dokument\"", html, StringComparison.Ordinal);
        Assert.Contains(">Anderes Dokument</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_OnPipedWikiLink_UsesDisplayInOutputAndTargetInHref()
    {
        string html = GzipHelper.Decompress(_sut.Parse("Siehe [[Ziel|Mein Anzeigetext]].").RenderedHtmlGz);

        Assert.Contains("href=\"mdexplorer://ziel\"", html, StringComparison.Ordinal);
        Assert.Contains(">Mein Anzeigetext</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_OnFrontmatterDocument_StripsFrontmatterFromHtml()
    {
        const string Markdown = """
            ---
            title: Mein Titel
            ---
            # Body
            """;

        string html = GzipHelper.Decompress(_sut.Parse(Markdown).RenderedHtmlGz);

        Assert.DoesNotContain("title:", html, StringComparison.Ordinal);
        Assert.DoesNotContain("---", html, StringComparison.Ordinal);
        Assert.Contains("<h1", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_StoresOutlinkSlugs()
    {
        ParseResult result = _sut.Parse("[[Foo Bar]] [[Foo Bar]] [[Café]]");

        Assert.Equal(["foo-bar", "café"], result.OutlinkSlugs);
    }

    [Fact]
    public void Parse_Performance_TenKilobyteMarkdown_RendersUnderFiftyMilliseconds()
    {
        string markdown = BuildLargeMarkdown(targetBytes: 10 * 1024);

        // Warm-up — JIT, Markdig static init, GZip pipeline.
        _ = _sut.Parse(markdown);

        Stopwatch stopwatch = Stopwatch.StartNew();
        ParseResult result = _sut.Parse(markdown);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds <= 50,
            $"Erwartet ≤ 50 ms, gemessen {stopwatch.ElapsedMilliseconds} ms.");
        Assert.NotEqual(0, result.RenderedHtmlGz.Length);
    }

    [Fact]
    public void Parse_HtmlBlobIsValidGzipStream()
    {
        ParseResult result = _sut.Parse("# Titel\n\nBody mit #tag.");

        string html = GzipHelper.Decompress(result.RenderedHtmlGz);

        Assert.Contains("<h1", html, StringComparison.Ordinal);
    }

    private static string BuildLargeMarkdown(int targetBytes)
    {
        const string Paragraph = "Dies ist ein Absatz mit **Fettdruck**, *Kursiv* und [[einem WikiLink]] sowie #foo.\n\n";
        StringBuilder builder = new(targetBytes + Paragraph.Length);
        while (builder.Length < targetBytes)
        {
            _ = builder.Append(Paragraph);
        }
        return builder.ToString();
    }
}
