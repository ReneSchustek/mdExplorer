using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;

namespace MdExplorer.Indexer.Tests.Fakes;

/// <summary>
/// Eine Sitzung auf dem <see cref="FakeMarkdownFileStore"/>, so wie ein DbContext je Scope: eigene
/// Vormerkliste, Speichern schreibt alles auf einmal oder nichts.
/// </summary>
/// <remarks>
/// Die frühere Attrappe teilte eine Instanz über alle Scopes und gab den gespeicherten Eintrag
/// selbst heraus. Damit waren Änderungen schon vor dem Speichern „persistiert“, und vorgemerkte
/// Änderungen eines gescheiterten Speicherns lagen beim nächsten Scope noch bereit. Tests zu
/// Fehlerfällen konnten so aus dem falschen Grund grün sein.
/// </remarks>
internal sealed class FakeMarkdownFileRepository(FakeMarkdownFileStore store) : IMarkdownFileRepository
{
    private readonly List<MarkdownFile> _pendingAdds = [];
    private readonly Dictionary<Guid, MarkdownFile> _pendingUpdates = [];
    private readonly List<MarkdownFile> _pendingRemoves = [];

    public Task<MarkdownFile?> GetByAbsolutePathAsync(string absolutePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        return Task.FromResult(store.FindByPath(absolutePath));
    }

    public Task<MarkdownFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(store.FindById(id));

    public Task<IReadOnlyList<MarkdownFile>> GetAllUnderRootAsync(string rootAbsolutePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootAbsolutePath);
        try
        {
            return Task.FromResult(store.FindUnder(rootAbsolutePath));
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException)
        {
            return Task.FromException<IReadOnlyList<MarkdownFile>>(ex);
        }
    }

    public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(store.Count());

    public Task AddAsync(MarkdownFile entity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _pendingAdds.Add(entity);
        return Task.CompletedTask;
    }

    public void Update(MarkdownFile entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (!_pendingAdds.Contains(entity))
        {
            _pendingUpdates[entity.Id] = entity;
        }
    }

    public void Remove(MarkdownFile entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _ = _pendingAdds.Remove(entity);
        _ = _pendingUpdates.Remove(entity.Id);
        _pendingRemoves.Add(entity);
    }

    // Wie die Datenschicht: sofort und ohne Vormerkliste. Sie leert dort den Change-Tracker; was
    // diese Sitzung vorgemerkt hatte, ist danach verworfen.
    public Task<int> RemoveRangeAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        int removed = store.RemoveRange(ids);
        Discard();
        return Task.FromResult(removed);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        await store.EnterSaveAsync().ConfigureAwait(false);
        int writes = store.Commit(_pendingAdds, _pendingUpdates.Values, _pendingRemoves);
        Discard();
        return writes;
    }

    private void Discard()
    {
        _pendingAdds.Clear();
        _pendingUpdates.Clear();
        _pendingRemoves.Clear();
    }
}
