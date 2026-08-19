using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;

namespace MdExplorer.Parser.Tests.Fakes;

internal sealed class FakeParseFailureRepository : IParseFailureRepository
{
    private readonly Dictionary<Guid, ParseFailure> _storeByFileId = [];
    private readonly List<ParseFailure> _pendingRecords = [];
    private readonly List<Guid> _pendingRemovals = [];

    public int LookupCallCount { get; private set; }

    public int RecordCallCount { get; private set; }

    public int RemoveCallCount { get; private set; }

    public int CountCallCount { get; private set; }

    public int SaveCallCount { get; private set; }

    public IReadOnlyDictionary<Guid, ParseFailure> Snapshot => _storeByFileId;

    // Legt einen Vermerk ohne SaveChanges in den Speicher — für Tests, die einen Bestand
    // vor dem ersten Durchlauf brauchen.
    public void SeedFailure(ParseFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        _storeByFileId[failure.MarkdownFileId] = failure;
    }

    public Task<IReadOnlyDictionary<Guid, ParseFailure>> GetByMarkdownFileIdsAsync(
        IReadOnlyCollection<Guid> markdownFileIds,
        CancellationToken cancellationToken)
    {
        LookupCallCount++;
        Dictionary<Guid, ParseFailure> result = [];
        foreach (Guid id in markdownFileIds)
        {
            if (_storeByFileId.TryGetValue(id, out ParseFailure? failure))
            {
                result[id] = failure;
            }
        }
        return Task.FromResult<IReadOnlyDictionary<Guid, ParseFailure>>(result);
    }

    public Task RecordAsync(ParseFailure failure, CancellationToken cancellationToken)
    {
        RecordCallCount++;
        _pendingRecords.Add(failure);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(IReadOnlyCollection<Guid> markdownFileIds, CancellationToken cancellationToken)
    {
        RemoveCallCount++;
        _pendingRemovals.AddRange(markdownFileIds);
        return Task.CompletedTask;
    }

    public Task<int> CountAsync(CancellationToken cancellationToken)
    {
        CountCallCount++;
        return Task.FromResult(_storeByFileId.Count);
    }

    // Wie im echten EF wirken Record und Remove erst mit SaveChanges.
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCallCount++;
        int writes = _pendingRecords.Count + _pendingRemovals.Count;
        foreach (ParseFailure record in _pendingRecords)
        {
            _storeByFileId[record.MarkdownFileId] = record;
        }
        foreach (Guid removal in _pendingRemovals)
        {
            _ = _storeByFileId.Remove(removal);
        }
        _pendingRecords.Clear();
        _pendingRemovals.Clear();
        return Task.FromResult(writes);
    }
}
