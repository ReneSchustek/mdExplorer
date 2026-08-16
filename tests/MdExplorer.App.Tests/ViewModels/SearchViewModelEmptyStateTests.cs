using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Tests für die beiden Leerzustände der Suche.
/// </summary>
/// <remarks>
/// „Noch nichts gesucht" und „nichts gefunden" sehen gleich aus und haben verschiedene
/// Ursachen. Wer sie verwechselt, hält seinen Bestand für leer und sucht am falschen Ende.
/// </remarks>
public sealed class SearchViewModelEmptyStateTests
{
    private static readonly TimeSpan TestDebounce = TimeSpan.FromMilliseconds(20);

    [Fact]
    public void WithoutAnyInput_ReportsNothingSearchedYet()
    {
        FakeSearchService searchService = new();
        using ServiceProvider provider = BuildProvider(searchService);
        using SearchViewModel sut = Create(provider);

        Assert.True(sut.ShowsNothingSearchedYet);
        Assert.False(sut.ShowsNoMatches);
    }

    [Fact]
    public void WithBlankInput_StillReportsNothingSearchedYet()
    {
        FakeSearchService searchService = new();
        using ServiceProvider provider = BuildProvider(searchService);
        using SearchViewModel sut = Create(provider);

        sut.QueryText = "   ";

        Assert.True(sut.ShowsNothingSearchedYet);
        Assert.False(sut.ShowsNoMatches);
    }

    [Fact]
    public async Task AfterAnAnswerWithoutHits_ReportsNoMatches()
    {
        FakeSearchService searchService = new();
        searchService.SetNextResults([]);
        using ServiceProvider provider = BuildProvider(searchService);
        using SearchViewModel sut = Create(provider);
        TaskCompletionSource completion = new();
        sut.SearchCompleted += (_, _) => completion.TrySetResult();

        sut.QueryText = "wortohnetreffer";
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(sut.ShowsNothingSearchedYet);
        Assert.True(sut.ShowsNoMatches);
    }

    [Fact]
    public async Task WithHits_ReportsNeitherEmptyState()
    {
        FakeSearchService searchService = new();
        searchService.SetNextResults([new SearchResult(Guid.NewGuid(), "A.md", "A", 1.0, "Fundstelle", [])]);
        using ServiceProvider provider = BuildProvider(searchService);
        using SearchViewModel sut = Create(provider);
        TaskCompletionSource completion = new();
        sut.SearchCompleted += (_, _) => completion.TrySetResult();

        sut.QueryText = "treffer";
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(sut.ShowsNothingSearchedYet);
        Assert.False(sut.ShowsNoMatches);
    }

    [Fact]
    public async Task WhileTypingTheNextQuery_DoesNotClaimNoMatchesYet()
    {
        FakeSearchService searchService = new();
        searchService.SetNextResults([]);
        using ServiceProvider provider = BuildProvider(searchService);
        using SearchViewModel sut = Create(provider);
        TaskCompletionSource completion = new();
        sut.SearchCompleted += (_, _) => completion.TrySetResult();

        sut.QueryText = "erste";
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.True(sut.ShowsNoMatches);

        // Neue Eingabe: Die Antwort von eben gilt der vorigen Anfrage und sagt über diese
        // nichts — bis die nächste Antwort da ist, wird nichts behauptet.
        sut.QueryText = "zweite";

        Assert.False(sut.ShowsNoMatches);
    }

    [Fact]
    public async Task ClearSearchCommand_EmptiesInputAndResults()
    {
        FakeSearchService searchService = new();
        searchService.SetNextResults([new SearchResult(Guid.NewGuid(), "A.md", "A", 1.0, "Fundstelle", [])]);
        using ServiceProvider provider = BuildProvider(searchService);
        using SearchViewModel sut = Create(provider);
        TaskCompletionSource completion = new();
        sut.SearchCompleted += (_, _) => completion.TrySetResult();

        sut.QueryText = "treffer";
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken).ConfigureAwait(true);

        sut.ClearSearchCommand.Execute(null);

        Assert.Empty(sut.QueryText);
        Assert.Empty(sut.Results);
        Assert.True(sut.ShowsNothingSearchedYet);
    }

    [Fact]
    public async Task WhenTheSearchItselfFails_SaysSo_InsteadOfClaimingNoHits()
    {
        using ServiceProvider provider = BuildProvider(new FailingSearchService());
        using SearchViewModel sut = Create(provider);
        TaskCompletionSource completion = new();
        sut.SearchCompleted += (_, _) => completion.TrySetResult();

        sut.QueryText = "irgendwas";
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(sut.ShowsSearchFailure);
        Assert.False(sut.ShowsNoMatches);
        Assert.False(sut.ShowsNothingSearchedYet);
    }

    [Fact]
    public async Task AfterAFailure_ANewQueryDropsTheFailureState()
    {
        using ServiceProvider provider = BuildProvider(new FailingSearchService());
        using SearchViewModel sut = Create(provider);
        TaskCompletionSource completion = new();
        sut.SearchCompleted += (_, _) => completion.TrySetResult();

        sut.QueryText = "irgendwas";
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.True(sut.ShowsSearchFailure);

        sut.ClearSearchCommand.Execute(null);

        Assert.False(sut.ShowsSearchFailure);
        Assert.True(sut.ShowsNothingSearchedYet);
    }

    private static SearchViewModel Create(ServiceProvider provider) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            new StrongReferenceMessenger(),
            NullLogger<SearchViewModel>.Instance,
            TestDebounce);

    private static ServiceProvider BuildProvider(ISearchService searchService)
    {
        ServiceCollection services = new();
        _ = services.AddScoped(_ => searchService);
        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>Eine Suche, die scheitert — für den Zustand, den ein leeres Ergebnis verdeckt.</summary>
    private sealed class FailingSearchService : ISearchService
    {
        public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Der Suchindex ist nicht ansprechbar.");
    }
}
