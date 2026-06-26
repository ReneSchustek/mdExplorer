using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>In-Memory-<see cref="ISearchService"/> mit konfigurierbarer Antwort und Call-Zähler.</summary>
internal sealed class FakeSearchService : ISearchService
{
    private readonly List<SearchResult> _next = [];

    public int CallCount { get; private set; }

    public List<SearchQuery> ReceivedQueries { get; } = [];

    public TimeSpan Delay { get; set; }

    public void SetNextResults(IEnumerable<SearchResult> results)
    {
        _next.Clear();
        _next.AddRange(results);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        CallCount++;
        ReceivedQueries.Add(query);
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
        }
        return _next.ToArray();
    }
}
