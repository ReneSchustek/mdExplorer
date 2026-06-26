using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;

namespace MdExplorer.Parser.Tests.Fakes;

internal sealed class FakeMarkdownDocumentRepository : IMarkdownDocumentRepository
{
    private readonly Dictionary<Guid, MarkdownDocument> _storeByFileId = [];
    private readonly List<MarkdownDocument> _pendingAdds = [];
    private readonly HashSet<MarkdownDocument> _pendingUpdates = [];

    public int SaveCallCount { get; private set; }

    public int ParseCount { get; private set; }

    public IReadOnlyDictionary<Guid, MarkdownDocument> Snapshot => _storeByFileId;

    // Im echten EF teilen docRepo und tagRepo denselben DbContext, ein einziger
    // SaveChanges committet beide. Der Test-Harness haengt hier die TagRepo-SaveChanges ein,
    // damit der Fake dasselbe Sichtbarkeitsmodell hat.
    public Func<CancellationToken, Task>? OnSaveChangesAsync { get; set; }

    public Task<MarkdownDocument?> GetByMarkdownFileIdAsync(Guid markdownFileId, CancellationToken cancellationToken)
    {
        _ = _storeByFileId.TryGetValue(markdownFileId, out MarkdownDocument? doc);
        return Task.FromResult(doc);
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
