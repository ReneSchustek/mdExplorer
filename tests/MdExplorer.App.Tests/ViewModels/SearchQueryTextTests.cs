using MdExplorer.App.ViewModels;
using MdExplorer.Search.Models;
using MdExplorer.Search.Services;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Die Ordner-Einschränkung, die die Oberfläche selbst in die Suche schreibt, kommt beim echten
/// Übersetzer als genau ein Pfad-Anfang an, mit dem Trennzeichen des Index.
/// </summary>
public sealed class SearchQueryTextTests
{
    [Theory]
    [InlineData("projekt/unterordner", @"projekt\unterordner\")]
    [InlineData(@"projekt\unterordner", @"projekt\unterordner\")]
    [InlineData("projekt/unterordner/", @"projekt\unterordner\")]
    [InlineData("mein ordner/2026", @"mein ordner\2026\")]
    [InlineData("docs", @"docs\")]
    public void PathFilter_ReachesTheQueryBuilderAsOnePrefixWithIndexSeparators(string folder, string expectedPrefix)
    {
        Fts5QueryPlan plan = new SearchQueryBuilder().Build(SearchQueryText.PathFilter(folder));

        Assert.Equal(expectedPrefix, Assert.Single(plan.PathPrefixes), StringComparer.Ordinal);
        Assert.Equal(string.Empty, plan.MatchExpression, StringComparer.Ordinal);
    }

    [Fact]
    public void PathFilter_WithAQuoteInTheName_KeepsTheQuote()
    {
        Fts5QueryPlan plan = new SearchQueryBuilder().Build(SearchQueryText.PathFilter("der \"beste\" ordner"));

        Assert.Equal("der \"beste\" ordner\\", Assert.Single(plan.PathPrefixes), StringComparer.Ordinal);
    }
}
