using Markdig.Syntax;
using MdExplorer.Parser.MarkdigExtensions;
using MdExplorer.Parser.Tests.Helpers;

namespace MdExplorer.Parser.Tests.MarkdigExtensions;

/// <summary>Direkte Unit-Tests des <see cref="WikiLinkInlineParser"/>.</summary>
public sealed class WikiLinkInlineParserTests
{
    [Fact]
    public void Match_OnSimpleWikiLink_ProducesInlineWithTargetEqualDisplay()
    {
        WikiLinkInline link = Assert.Single(ParseWikiLinks("Siehe [[Foo]] hier."));

        Assert.Equal("Foo", link.Target);
        Assert.Equal("Foo", link.Display);
    }

    [Fact]
    public void Match_OnPipedWikiLink_ProducesInlineWithSeparateTargetAndDisplay()
    {
        WikiLinkInline link = Assert.Single(ParseWikiLinks("Siehe [[Foo|Anzeige]] hier."));

        Assert.Equal("Foo", link.Target);
        Assert.Equal("Anzeige", link.Display);
    }

    [Fact]
    public void Match_OnEmptyContent_DoesNotMatch()
    {
        Assert.Empty(ParseWikiLinks("Leer: [[]] hier."));
    }

    [Fact]
    public void Match_OnContentOverLimit_DoesNotMatch()
    {
        string oversized = new('a', WikiLinkInlineParser.MaxContentLength + 1);
        Assert.Empty(ParseWikiLinks($"Vor [[{oversized}]] nach."));
    }

    [Fact]
    public void Match_OnNewlineInContent_DoesNotMatch()
    {
        Assert.Empty(ParseWikiLinks("Vor [[a\nb]] nach."));
    }

    [Fact]
    public void Match_OnNestedOpeningBracket_DoesNotMatch()
    {
        Assert.Empty(ParseWikiLinks("Vor [[a[b]] nach."));
    }

    [Fact]
    public void Match_OnMissingClosingBrackets_DoesNotMatch()
    {
        Assert.Empty(ParseWikiLinks("Vor [[abc und der Rest."));
    }

    [Fact]
    public void Match_OnEmptyTargetWithPipe_DoesNotMatch()
    {
        Assert.Empty(ParseWikiLinks("Vor [[|display]] nach."));
    }

    [Fact]
    public void Match_OnEmptyDisplayWithPipe_DoesNotMatch()
    {
        Assert.Empty(ParseWikiLinks("Vor [[target|]] nach."));
    }

    [Fact]
    public void Match_OnSingleBrackets_DoesNotMatch()
    {
        // Normaler Markdown-Link [abc] erzeugt keinen WikiLinkInline-Knoten.
        Assert.Empty(ParseWikiLinks("Vor [abc] nach."));
    }

    private static IReadOnlyList<WikiLinkInline> ParseWikiLinks(string markdown)
    {
        MarkdownDocument ast = TestPipelineFactory.Parse(markdown);
        return [.. ast.Descendants<WikiLinkInline>()];
    }
}
