using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;

namespace MdExplorer.Indexer.Tests.Fakes;

/// <summary>
/// In-Memory-Repository für Indexer-Unit-Tests.
/// Indiziert nach Primärschlüssel (analog zu EF-Tracking) und liefert eine
/// Snapshot-Ansicht nach AbsolutePath für Asserts.
/// </summary>
internal sealed class FakeMarkdownFileRepository : IMarkdownFileRepository, IDisposable
{
    private readonly Dictionary<Guid, MarkdownFile> _store = [];
    private readonly List<MarkdownFile> _pendingAdds = [];
    private readonly HashSet<MarkdownFile> _pendingUpdates = [];
    private readonly List<MarkdownFile> _pendingRemoves = [];
    private readonly SemaphoreSlim _saveSignal = new(0, int.MaxValue);

    public int TotalWrites { get; private set; }

    public int SaveCallCount { get; private set; }

    /// <summary>Aktueller Stand der persistierten Dateien, indiziert nach <c>AbsolutePath</c>.</summary>
    public IReadOnlyDictionary<string, MarkdownFile> Snapshot =>
        _store.Values.ToDictionary(file => file.AbsolutePath, StringComparer.OrdinalIgnoreCase);

    public Task<bool> WaitForNextSaveAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        _saveSignal.WaitAsync(timeout, cancellationToken);

    public void Dispose() => _saveSignal.Dispose();

    public Task<MarkdownFile?> GetByAbsolutePathAsync(string absolutePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        MarkdownFile? match = _store.Values
            .FirstOrDefault(file => string.Equals(file.AbsolutePath, absolutePath, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match);
    }

    public Task<MarkdownFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_store.TryGetValue(id, out MarkdownFile? file) ? file : null);

    public Task<IReadOnlyList<MarkdownFile>> GetAllUnderRootAsync(string rootAbsolutePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootAbsolutePath);
        List<MarkdownFile> result =
        [
            .. _store.Values.Where(file => file.AbsolutePath.StartsWith(rootAbsolutePath, StringComparison.OrdinalIgnoreCase)),
        ];
        return Task.FromResult<IReadOnlyList<MarkdownFile>>(result);
    }

    public Task<Guid?> FindIdByFileNameAsync(string fileNameWithoutExtension, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileNameWithoutExtension);
        MarkdownFile? match = _store.Values
            .Where(file => string.Equals(file.FileNameWithoutExtension, fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return Task.FromResult<Guid?>(match?.Id);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_store.Count);

    public Task AddAsync(MarkdownFile entity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _pendingAdds.Add(entity);
        return Task.CompletedTask;
    }

    public void Update(MarkdownFile entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _ = _pendingUpdates.Add(entity);
    }

    public void Remove(MarkdownFile entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _pendingRemoves.Add(entity);
    }

    /// <summary>Optionaler Throw bei N-tem <see cref="SaveChangesAsync"/>-Aufruf (Test-Hook).</summary>
    public Exception? ThrowOnNextSave { get; set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCallCount++;
        Exception? toThrow = ThrowOnNextSave;
        if (toThrow is not null)
        {
            ThrowOnNextSave = null;
            return Task.FromException<int>(toThrow);
        }
        int writes = _pendingAdds.Count + _pendingUpdates.Count + _pendingRemoves.Count;
        foreach (MarkdownFile add in _pendingAdds)
        {
            _store[add.Id] = add;
        }
        foreach (MarkdownFile update in _pendingUpdates)
        {
            _store[update.Id] = update;
        }
        foreach (MarkdownFile remove in _pendingRemoves)
        {
            _ = _store.Remove(remove.Id);
        }
        _pendingAdds.Clear();
        _pendingUpdates.Clear();
        _pendingRemoves.Clear();
        TotalWrites += writes;
        _ = _saveSignal.Release();
        return Task.FromResult(writes);
    }
}
