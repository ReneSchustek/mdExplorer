using Markdig.Syntax;
using MdExplorer.Parser.Services;
using MdExplorer.Parser.Tests.Helpers;

namespace MdExplorer.Parser.Tests.Services;

public sealed class WikiLinkExtractorTests
{
    private readonly WikiLinkExtractor _sut = new();

    [Fact]
    public void Extract_OnSimpleLink_ReturnsTarget()
    {
        MarkdownDocument ast = TestPipelineFactory.Parse("Siehe [[Anderes Dokument]].");

        IReadOnlyList<string> result = _sut.Extract(ast);

        _ = Assert.Single(result);
        Assert.Equal("Anderes Dokument", result[0]);
    }

    [Fact]
    public void Extract_OnPipedLink_ReturnsTargetNotDisplay()
    {
        MarkdownDocument ast = TestPipelineFactory.Parse("Siehe [[Ziel|Anzeige]].");

        IReadOnlyList<string> result = _sut.Extract(ast);

        Assert.Equal("Ziel", Assert.Single(result));
    }

    [Fact]
    public void Extract_DeduplicatesSameTarget()
    {
        MarkdownDocument ast = TestPipelineFactory.Parse("[[A]] und [[A]] und [[B]].");

        IReadOnlyList<string> result = _sut.Extract(ast);

        Assert.Equal(["A", "B"], result);
    }

    [Fact]
    public void Extract_OnEmptyTarget_Skips()
    {
        MarkdownDocument ast = TestPipelineFactory.Parse("Leer: [[]] und [[ |X]] und [[Y| ]].");

        IReadOnlyList<string> result = _sut.Extract(ast);

        Assert.Empty(result);
    }

    [Fact]
    public void Extract_OnInlineCode_Skips()
    {
        MarkdownDocument ast = TestPipelineFactory.Parse("Im Text `[[NichtErfasst]]` aber [[Erfasst]].");

        IReadOnlyList<string> result = _sut.Extract(ast);

        Assert.Equal("Erfasst", Assert.Single(result));
    }

    [Fact]
    public void Extract_OnNullAst_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => _sut.Extract(null!));
    }
}
