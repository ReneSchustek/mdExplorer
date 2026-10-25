using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>In-Memory-<see cref="ISearchService"/> mit konfigurierbarer Antwort und Call-Zähler.</summary>
internal sealed class FakeSearchService : ISearchService
{
    private readonly List<SearchResult> _next = [];

    public int CallCount { get; private set; }

    public List<SearchQuery> ReceivedQueries { get; } = [];

    public void SetNextResults(IEnumerable<SearchResult> results)
    {
        _next.Clear();
        _next.AddRange(results);
    }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        CallCount++;
        ReceivedQueries.Add(query);
        return Task.FromResult<IReadOnlyList<SearchResult>>([.. _next]);
    }
}
