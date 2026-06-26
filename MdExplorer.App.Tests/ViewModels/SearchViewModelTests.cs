using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Tests für die Debounce- und Concurrency-Logik des <see cref="SearchViewModel"/>.
/// Verwenden <see cref="FakeTimeProvider"/> (eigene Implementierung), damit die Asserts
/// deterministisch sind und keine echte Wartezeit nötig ist.
/// </summary>
public sealed class SearchViewModelTests
{
    private static readonly TimeSpan TestDebounce = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task OnTextChanged_DebouncesQuery_FiresOnceForRapidInputs()
    {
        FakeSearchService searchService = new();
        searchService.SetNextResults([]);

        using ServiceProvider provider = BuildProvider(searchService);
        using SearchViewModel vm = new(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, new StrongReferenceMessenger(), NullLogger<SearchViewModel>.Instance, TestDebounce);
        TaskCompletionSource completion = new();
        vm.SearchCompleted += (_, _) => completion.TrySetResult();

        vm.QueryText = "f";
        vm.QueryText = "fo";
        vm.QueryText = "foo";

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(true);

        Assert.Equal(1, searchService.CallCount);
        Assert.Equal("foo", searchService.ReceivedQueries[0].Text);
    }

    [Fact]
    public async Task OnSearchResult_PopulatesItemsInOrder()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        FakeSearchService searchService = new();
        searchService.SetNextResults(
        [
            new SearchResult(first, "A.md", "A", 1.0, "snippet A", []),
            new SearchResult(second, "B.md", "B", 2.0, "snippet B", []),
        ]);

        using ServiceProvider provider = BuildProvider(searchService);
        using SearchViewModel vm = new(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, new StrongReferenceMessenger(), NullLogger<SearchViewModel>.Instance, TestDebounce);
        TaskCompletionSource completion = new();
        vm.SearchCompleted += (_, _) => completion.TrySetResult();

        vm.QueryText = "foo";
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(true);

        Assert.Equal(2, vm.Results.Count);
        Assert.Equal(first, vm.Results[0].MarkdownFileId);
        Assert.Equal(second, vm.Results[1].MarkdownFileId);
    }

    [Fact]
    public async Task OnPathPrefixFilter_FiltersResults_WhenScopeEnabled()
    {
        Guid included = Guid.NewGuid();
        Guid excluded = Guid.NewGuid();
        FakeSearchService searchService = new();
        searchService.SetNextResults(
        [
            new SearchResult(included, "projekte/a.md", "A", 1.0, "x", []),
            new SearchResult(excluded, "anderes/b.md", "B", 1.0, "x", []),
        ]);

        using ServiceProvider provider = BuildProvider(searchService);
        using SearchViewModel vm = new(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, new StrongReferenceMessenger(), NullLogger<SearchViewModel>.Instance, TestDebounce);
        vm.PathPrefixFilter = "projekte/";
        vm.ScopeToSelectedFolder = true;
        TaskCompletionSource completion = new();
        vm.SearchCompleted += (_, _) => completion.TrySetResult();

        vm.QueryText = "foo";
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(true);

        _ = Assert.Single(vm.Results);
        Assert.Equal(included, vm.Results[0].MarkdownFileId);
    }

    [Fact]
    public async Task OnPathPrefixFilter_Ignored_WhenScopeDisabled()
    {
        // Default-Verhalten: ohne aktiven Scope wird der Pfad-Prefix ignoriert und global gesucht.
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        FakeSearchService searchService = new();
        searchService.SetNextResults(
        [
            new SearchResult(first, "projekte/a.md", "A", 1.0, "x", []),
            new SearchResult(second, "anderes/b.md", "B", 1.0, "x", []),
        ]);

        using ServiceProvider provider = BuildProvider(searchService);
        using SearchViewModel vm = new(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, new StrongReferenceMessenger(), NullLogger<SearchViewModel>.Instance, TestDebounce);
        vm.PathPrefixFilter = "projekte/";
        TaskCompletionSource completion = new();
        vm.SearchCompleted += (_, _) => completion.TrySetResult();

        vm.QueryText = "foo";
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(true);

        Assert.Equal(2, vm.Results.Count);
    }

    private static ServiceProvider BuildProvider(FakeSearchService searchService)
    {
        ServiceCollection services = new();
        _ = services.AddScoped<ISearchService>(_ => searchService);
        return services.BuildServiceProvider(validateScopes: true);
    }
}
