using MdExplorer.Search.Models;
using MdExplorer.Search.Services;

namespace MdExplorer.Search.Tests.Services;

/// <summary>Unit-Tests für <see cref="SnippetExtractor"/>.</summary>
public sealed class SnippetExtractorTests
{

    [Fact]
    public void Extract_OnEmptyString_ReturnsEmptyList()
    {
        IReadOnlyList<SearchHighlight> result = SnippetExtractor.Extract(string.Empty);

        Assert.Empty(result);
    }

    [Fact]
    public void Extract_OnNoMarkers_ReturnsEmptyList()
    {
        IReadOnlyList<SearchHighlight> result = SnippetExtractor.Extract("kein highlight enthalten");

        Assert.Empty(result);
    }

    [Fact]
    public void Extract_OnSingleHighlight_ReportsCorrectPosition()
    {
        string snippet = "alpha <mark>treffer</mark> beta";

        IReadOnlyList<SearchHighlight> result = SnippetExtractor.Extract(snippet);

        _ = Assert.Single(result);
        Assert.Equal(6, result[0].Start);
        Assert.Equal("treffer".Length, result[0].Length);
    }

    [Fact]
    public void Extract_OnMultipleHighlights_ReturnsAllInOrder()
    {
        string snippet = "<mark>foo</mark> mitte <mark>bar</mark>";

        IReadOnlyList<SearchHighlight> result = SnippetExtractor.Extract(snippet);

        Assert.Equal(2, result.Count);
        Assert.Equal(0, result[0].Start);
        Assert.Equal(3, result[0].Length);
        Assert.True(result[1].Start > result[0].Start);
        Assert.Equal(3, result[1].Length);
    }

    [Fact]
    public void Extract_OnUnclosedMarker_IgnoresIncompleteHighlight()
    {
        string snippet = "alpha <mark>treffer ohne ende";

        IReadOnlyList<SearchHighlight> result = SnippetExtractor.Extract(snippet);

        Assert.Empty(result);
    }

    [Fact]
    public void Extract_OnNull_ThrowsArgumentNullException()
    {
        _ = Assert.Throws<ArgumentNullException>(() => SnippetExtractor.Extract(null!));
    }
}
