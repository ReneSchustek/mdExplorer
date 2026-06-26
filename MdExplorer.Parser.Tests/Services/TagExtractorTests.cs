using Markdig.Syntax;
using MdExplorer.Parser.Services;
using MdExplorer.Parser.Tests.Helpers;
using AppSettings = MdExplorer.Core.Models.AppSettings;

namespace MdExplorer.Parser.Tests.Services;

public sealed class TagExtractorTests
{
    private readonly FakeSettingsService _settings = new();
    private readonly TagExtractor _sut;

    public TagExtractorTests()
    {
        _sut = new TagExtractor(_settings);
    }

    [Fact]
    public void ExtractFromAst_OnBodyText_FindsTag()
    {
        MarkdownDocument ast = TestPipelineFactory.Parse("Einleitung mit #foo Tag.");

        IReadOnlyList<string> result = _sut.ExtractFromAst(ast);

        Assert.Equal("foo", Assert.Single(result));
    }

    [Fact]
    public void ExtractFromAst_OnInlineCode_DoesNotMatchHashtag()
    {
        MarkdownDocument ast = TestPipelineFactory.Parse("Im Inline-Code `#nottag` und im Body #yestag.");

        IReadOnlyList<string> result = _sut.ExtractFromAst(ast);

        Assert.Equal("yestag", Assert.Single(result));
    }

    [Fact]
    public void ExtractFromAst_OnCodeBlock_DoesNotMatchHashtag()
    {
        const string Markdown = """
            Body mit #ja-tag.

            ```
            #nein-tag
            ```

            Und nochmals #weitertag.
            """;

        IReadOnlyList<string> result = _sut.ExtractFromAst(TestPipelineFactory.Parse(Markdown));

        Assert.Equal(["ja-tag", "weitertag"], result);
    }

    [Fact]
    public void ExtractFromAst_OnFrontmatter_DoesNotMatchHashtag()
    {
        const string Markdown = """
            ---
            tags: [foo, bar]
            ---
            Body mit #echter-tag.
            """;

        IReadOnlyList<string> result = _sut.ExtractFromAst(TestPipelineFactory.Parse(Markdown));

        Assert.Equal("echter-tag", Assert.Single(result));
    }

    [Fact]
    public void ExtractFromAst_OnUmlauts_PreservesTag()
    {
        MarkdownDocument ast = TestPipelineFactory.Parse("Tag #München und #über-uns.");

        IReadOnlyList<string> result = _sut.ExtractFromAst(ast);

        Assert.Equal(["München", "über-uns"], result);
    }

    [Fact]
    public void ExtractFromAst_OnSingleCharTag_Skips()
    {
        MarkdownDocument ast = TestPipelineFactory.Parse("Zu kurz: #a (übersehen). Ok: #ab.");

        IReadOnlyList<string> result = _sut.ExtractFromAst(ast);

        Assert.Equal("ab", Assert.Single(result));
    }

    [Fact]
    public void ExtractFromAst_DeduplicatesCaseInsensitively()
    {
        MarkdownDocument ast = TestPipelineFactory.Parse("#Foo und #foo und #FOO und #bar.");

        IReadOnlyList<string> result = _sut.ExtractFromAst(ast);

        Assert.Equal(["Foo", "bar"], result);
    }

    [Fact]
    public void ExtractFromAst_OnNullAst_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => _sut.ExtractFromAst(null!));
    }

    [Fact]
    public async Task ExtractFromAst_WhenAutoExtractDisabled_ReturnsEmpty()
    {
        AppSettings disabled = AppSettings.Default with
        {
            Indexing = AppSettings.Default.Indexing with { AutoExtractHashtags = false },
        };
        await _settings.SaveAsync(disabled, CancellationToken.None).ConfigureAwait(true);
        MarkdownDocument ast = TestPipelineFactory.Parse("Body mit #foo und #bar.");

        IReadOnlyList<string> result = _sut.ExtractFromAst(ast);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ExtractFromText_WhenAutoExtractDisabled_ReturnsEmpty()
    {
        AppSettings disabled = AppSettings.Default with
        {
            Indexing = AppSettings.Default.Indexing with { AutoExtractHashtags = false },
        };
        await _settings.SaveAsync(disabled, CancellationToken.None).ConfigureAwait(true);

        IReadOnlyList<string> result = _sut.ExtractFromText("Body mit #foo und #bar.");

        Assert.Empty(result);
    }
}
