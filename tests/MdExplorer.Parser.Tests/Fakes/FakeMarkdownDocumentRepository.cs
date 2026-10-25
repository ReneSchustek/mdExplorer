using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;

namespace MdExplorer.Parser.Tests.Fakes;

/// <summary>
/// Dokument-Repository im Arbeitsspeicher; Hinzufügen und Ändern wirken erst mit
/// <c>SaveChangesAsync</c>. Die Zähler zeigen, wie der Orchestrator nachfragt.
/// </summary>
internal sealed class FakeMarkdownDocumentRepository : IMarkdownDocumentRepository
{
    private readonly Dictionary<Guid, MarkdownDocument> _storeByFileId = [];
    private readonly List<MarkdownDocument> _pendingAdds = [];
    private readonly HashSet<MarkdownDocument> _pendingUpdates = [];

    public int SaveCallCount { get; private set; }

    public int ParseCount { get; private set; }

    // Einzelabfragen je Datei; der Orchestrator fragt gesammelt, die Zahl bleibt bei 0.
    public int PointLookupCallCount { get; private set; }

    // Gesammelte Abfragen; eine je Portion, in der veraltete Dokumente stehen.
    public int BatchLookupCallCount { get; private set; }

    public IReadOnlyDictionary<Guid, MarkdownDocument> Snapshot => _storeByFileId;

    // Mit EF teilen sich Dokument- und Schlagwort-Repository einen DbContext, und ein einziges
    // SaveChanges schreibt beide. Der Prüfstand hängt hier das Speichern der Schlagworte ein,
    // damit die Attrappen dasselbe sehen wie die echten Repositories.
    public Func<CancellationToken, Task>? OnSaveChangesAsync { get; set; }

    public Task<MarkdownDocument?> GetByMarkdownFileIdAsync(Guid markdownFileId, CancellationToken cancellationToken)
    {
        PointLookupCallCount++;
        _ = _storeByFileId.TryGetValue(markdownFileId, out MarkdownDocument? doc);
        return Task.FromResult(doc);
    }

    public Task<IReadOnlyDictionary<Guid, MarkdownDocument>> GetByMarkdownFileIdsAsync(
        IReadOnlyCollection<Guid> markdownFileIds,
        CancellationToken cancellationToken)
    {
        BatchLookupCallCount++;
        Dictionary<Guid, MarkdownDocument> result = [];
        foreach (Guid id in markdownFileIds)
        {
            if (_storeByFileId.TryGetValue(id, out MarkdownDocument? doc))
            {
                result[id] = doc;
            }
        }
        return Task.FromResult<IReadOnlyDictionary<Guid, MarkdownDocument>>(result);
    }

    public Task<IReadOnlyList<Guid>> GetStaleOrMissingAsync(IReadOnlyDictionary<Guid, string> hashesByFileId, CancellationToken cancellationToken)
    {
        List<Guid> stale = [];
        foreach (KeyValuePair<Guid, string> wanted in hashesByFileId)
        {
            if (!_storeByFileId.TryGetValue(wanted.Key, out MarkdownDocument? existing)
                || existing.SourceContentHash != wanted.Value)
            {
                stale.Add(wanted.Key);
            }
        }
        return Task.FromResult<IReadOnlyList<Guid>>(stale);
    }

    public Task AddAsync(MarkdownDocument document, CancellationToken cancellationToken)
    {
        _pendingAdds.Add(document);
        ParseCount++;
        return Task.CompletedTask;
    }

    public void Update(MarkdownDocument document)
    {
        _ = _pendingUpdates.Add(document);
        ParseCount++;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCallCount++;
        int writes = _pendingAdds.Count + _pendingUpdates.Count;
        foreach (MarkdownDocument add in _pendingAdds)
        {
            _storeByFileId[add.MarkdownFileId] = add;
        }
        foreach (MarkdownDocument update in _pendingUpdates)
        {
            _storeByFileId[update.MarkdownFileId] = update;
        }
        _pendingAdds.Clear();
        _pendingUpdates.Clear();
        if (OnSaveChangesAsync is not null)
        {
            await OnSaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        return writes;
    }
}
