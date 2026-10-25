using MdExplorer.App.Tests.Fakes;
using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Eine leere Trefferliste meldet, warum sie leer ist: noch nichts gesucht, nichts gefunden
/// oder die Suche selbst gescheitert.
/// </summary>
/// <remarks>
/// „Noch nichts gesucht" und „nichts gefunden" sehen gleich aus und haben verschiedene
/// Ursachen. Wer sie verwechselt, hält seinen Bestand für leer und sucht am falschen Ende.
/// </remarks>
public sealed class SearchViewModelEmptyStateTests
{
    private static readonly Guid HitId = new("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void WithoutAnyInput_ReportsNothingSearchedYet()
    {
        using SearchTestHarness harness = new();

        Assert.True(harness.ViewModel.ShowsNothingSearchedYet);
        Assert.False(harness.ViewModel.ShowsNoMatches);
    }

    [Fact]
    public void WithBlankInput_StillReportsNothingSearchedYet()
    {
        using SearchTestHarness harness = new();

        harness.Type("   ");

        Assert.True(harness.ViewModel.ShowsNothingSearchedYet);
        Assert.False(harness.ViewModel.ShowsNoMatches);
        Assert.Equal(0, harness.Recording.CallCount);
    }

    [Fact]
    public void AfterAnAnswerWithoutHits_ReportsNoMatches()
    {
        using SearchTestHarness harness = new();

        harness.Type("wortohnetreffer");

        Assert.False(harness.ViewModel.ShowsNothingSearchedYet);
        Assert.True(harness.ViewModel.ShowsNoMatches);
    }

    [Fact]
    public void WithHits_ReportsNeitherEmptyState()
    {
        using SearchTestHarness harness = new();
        harness.Recording.SetNextResults([new SearchResult(HitId, "A.md", "A", 1.0, "Fundstelle")]);

        harness.Type("treffer");

        Assert.False(harness.ViewModel.ShowsNothingSearchedYet);
        Assert.False(harness.ViewModel.ShowsNoMatches);
    }

    [Fact]
    public void WhileTypingTheNextQuery_DoesNotClaimNoMatchesYet()
    {
        using SearchTestHarness harness = new();
        harness.Type("erste");
        Assert.True(harness.ViewModel.ShowsNoMatches);

        // Neue Eingabe: Die Antwort von eben gilt der vorigen Anfrage und sagt über diese
        // nichts; bis die nächste Antwort da ist, wird nichts behauptet.
        harness.ViewModel.QueryText = "zweite";

        Assert.False(harness.ViewModel.ShowsNoMatches);
        Assert.True(harness.ViewModel.IsSearching);
    }

    [Fact]
    public void ClearSearchCommand_EmptiesInputAndResults()
    {
        using SearchTestHarness harness = new();
        harness.Recording.SetNextResults([new SearchResult(HitId, "A.md", "A", 1.0, "Fundstelle")]);
        harness.Type("treffer");

        harness.ViewModel.ClearSearchCommand.Execute(null);

        Assert.Empty(harness.ViewModel.QueryText);
        Assert.Empty(harness.ViewModel.Results);
        Assert.True(harness.ViewModel.ShowsNothingSearchedYet);
    }

    [Theory]
    [InlineData(TestFailures.Database)]
    [InlineData(TestFailures.InvalidState)]
    public void WhenTheSearchItselfFails_SaysSo_InsteadOfClaimingNoHits(string kind)
    {
        using SearchTestHarness harness = new(new FailingSearchService(TestFailures.Of(kind)));

        harness.Type("irgendwas");

        Assert.True(harness.ViewModel.ShowsSearchFailure);
        Assert.False(harness.ViewModel.IsSearching);
        Assert.False(harness.ViewModel.ShowsNoMatches);
        Assert.False(harness.ViewModel.ShowsNothingSearchedYet);
    }

    [Fact]
    public void AfterAFailure_ClearingTheFieldDropsTheFailureState()
    {
        using SearchTestHarness harness = new(new FailingSearchService(TestFailures.Of(TestFailures.Database)));
        harness.Type("irgendwas");
        Assert.True(harness.ViewModel.ShowsSearchFailure);

        harness.ViewModel.ClearSearchCommand.Execute(null);

        Assert.False(harness.ViewModel.ShowsSearchFailure);
        Assert.True(harness.ViewModel.ShowsNothingSearchedYet);
    }

    [Fact]
    public void AfterAFailure_TypingANewQueryDropsTheFailureState()
    {
        using SearchTestHarness harness = new(new FailingSearchService(TestFailures.Of(TestFailures.Database)));
        harness.Type("irgendwas");

        harness.ViewModel.QueryText = "anderes";

        Assert.False(harness.ViewModel.ShowsSearchFailure);
        Assert.True(harness.ViewModel.IsSearching);
    }

    /// <summary>Eine Suche, die scheitert, für den Zustand, den ein leeres Ergebnis verdeckt.</summary>
    private sealed class FailingSearchService(Exception failure) : ISearchService
    {
        public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<SearchResult>>(failure);
    }
}
