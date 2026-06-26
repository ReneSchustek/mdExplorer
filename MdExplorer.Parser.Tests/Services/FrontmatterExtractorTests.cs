using Markdig.Syntax;
using MdExplorer.Parser.Services;
using MdExplorer.Parser.Tests.Helpers;

namespace MdExplorer.Parser.Tests.Services;

public sealed class FrontmatterExtractorTests
{
    private readonly FrontmatterExtractor _sut = new();

    [Fact]
    public void Extract_OnMissingFrontmatter_ReturnsEmpty()
    {
        MarkdownDocument ast = TestPipelineFactory.Parse("# Nur Body, kein Frontmatter.");

        IReadOnlyDictionary<string, string> result = _sut.Extract(ast);

        Assert.Empty(result);
    }

    [Fact]
    public void Extract_OnScalarValues_FillsDictionary()
    {
        const string Markdown = """
            ---
            title: Mein Titel
            author: Rene
            ---
            # Body
            """;

        IReadOnlyDictionary<string, string> result = _sut.Extract(TestPipelineFactory.Parse(Markdown));

        Assert.Equal("Mein Titel", result["title"]);
        Assert.Equal("Rene", result["author"]);
    }

    [Fact]
    public void Extract_OnYamlList_JoinsCommaSeparated()
    {
        const string Markdown = """
            ---
            tags:
              - foo
              - bar
              - baz
            ---
            Body
            """;

        IReadOnlyDictionary<string, string> result = _sut.Extract(TestPipelineFactory.Parse(Markdown));

        Assert.Equal("foo, bar, baz", result["tags"]);
    }

    [Fact]
    public void Extract_OnInlineList_ReturnsFlattenedValue()
    {
        const string Markdown = """
            ---
            tags: [foo, bar]
            ---
            Body
            """;

        IReadOnlyDictionary<string, string> result = _sut.Extract(TestPipelineFactory.Parse(Markdown));

        Assert.Equal("foo, bar", result["tags"]);
    }

    [Fact]
    public void Extract_KeyLookupIsCaseInsensitive()
    {
        const string Markdown = """
            ---
            Title: X
            ---
            """;

        IReadOnlyDictionary<string, string> result = _sut.Extract(TestPipelineFactory.Parse(Markdown));

        Assert.Equal("X", result["title"]);
        Assert.Equal("X", result["TITLE"]);
    }

    [Fact]
    public void Extract_OnNestedMapping_SkipsKeyWithoutCrashing()
    {
        const string Markdown = """
            ---
            meta:
              nested: value
            title: Top
            ---
            """;

        IReadOnlyDictionary<string, string> result = _sut.Extract(TestPipelineFactory.Parse(Markdown));

        Assert.Equal("Top", result["title"]);
        Assert.False(result.ContainsKey("meta"));
    }

    [Fact]
    public void Extract_OnNullAst_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => _sut.Extract(null!));
    }
}
