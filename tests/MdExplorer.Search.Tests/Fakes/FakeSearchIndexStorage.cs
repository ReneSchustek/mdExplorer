using MdExplorer.Core.Abstractions;

namespace MdExplorer.Search.Tests.Fakes;

/// <summary>
/// Suchindex, der jede Abfrage mit den hinterlegten Dokumenten in Einfügereihenfolge
/// beantwortet, wie der echte Speicher begrenzt auf <c>Skip</c> und <c>Take</c>, und Abfragen
/// wie Rumpf-Ladevorgänge mitschreibt. Den Ausdruck wertet er nicht aus; die Auswahl trifft
/// damit allein der Dienst.
/// </summary>
internal sealed class FakeSearchIndexStorage : ISearchIndexStorage
{
    private readonly Dictionary<Guid, (string Path, string Title, string Body)> _docs = new();
    private readonly List<Guid> _order = [];

    public List<SearchIndexQuery> QueryCalls { get; } = [];

    public List<IReadOnlyCollection<Guid>> LoadBodiesCalls { get; } = [];

    public void AddHit(Guid id, string path, string title, string body)
    {
        _docs[id] = (path, title, body);
        _order.Add(id);
    }

    public Task<IReadOnlyDictionary<Guid, string>> LoadIndexedHashesAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task ApplyChangesAsync(
        IReadOnlyCollection<Guid> deletes,
        IReadOnlyCollection<SearchIndexEntry> upserts,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<SearchIndexHit>> QueryAsync(SearchIndexQuery query, CancellationToken cancellationToken)
    {
        QueryCalls.Add(query);
        List<SearchIndexHit> hits = [];
        foreach (Guid id in _order.Skip(query.Skip).Take(query.Take))
        {
            (string Path, string Title, string Body) doc = _docs[id];
            hits.Add(new SearchIndexHit(id, doc.Path, doc.Title, string.Empty, 0.0));
        }
        return Task.FromResult<IReadOnlyList<SearchIndexHit>>(hits);
    }

    public Task<IReadOnlyDictionary<Guid, string>> LoadBodiesAsync(
        IReadOnlyCollection<Guid> markdownFileIds,
        CancellationToken cancellationToken)
    {
        LoadBodiesCalls.Add(markdownFileIds);
        Dictionary<Guid, string> bodies = new();
        foreach (Guid id in markdownFileIds)
        {
            if (_docs.TryGetValue(id, out (string Path, string Title, string Body) doc))
            {
                bodies[id] = doc.Body;
            }
        }
        return Task.FromResult<IReadOnlyDictionary<Guid, string>>(bodies);
    }
}
