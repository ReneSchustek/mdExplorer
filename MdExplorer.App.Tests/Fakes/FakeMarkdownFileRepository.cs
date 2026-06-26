using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>In-Memory-Repo für WikiLink-Auflösungstests.</summary>
internal sealed class FakeMarkdownFileRepository : IMarkdownFileRepository
{
    private readonly Dictionary<Guid, MarkdownFile> _store = [];

    public void Add(MarkdownFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        _store[file.Id] = file;
    }

    public Task<MarkdownFile?> GetByAbsolutePathAsync(string absolutePath, CancellationToken cancellationToken) =>
        Task.FromResult(_store.Values.FirstOrDefault(f => string.Equals(f.AbsolutePath, absolutePath, StringComparison.OrdinalIgnoreCase)));

    public Task<MarkdownFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_store.TryGetValue(id, out MarkdownFile? file) ? file : null);

    public Task<IReadOnlyList<MarkdownFile>> GetAllUnderRootAsync(string rootAbsolutePath, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MarkdownFile>>([.. _store.Values]);

    public Task<Guid?> FindIdByFileNameAsync(string fileNameWithoutExtension, CancellationToken cancellationToken)
    {
        MarkdownFile? match = _store.Values
            .Where(f => string.Equals(f.FileNameWithoutExtension, fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return Task.FromResult<Guid?>(match?.Id);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(_store.Count);

    public Task AddAsync(MarkdownFile entity, CancellationToken cancellationToken) { Add(entity); return Task.CompletedTask; }
    public void Update(MarkdownFile entity) => Add(entity);
    public void Remove(MarkdownFile entity) { ArgumentNullException.ThrowIfNull(entity); _ = _store.Remove(entity.Id); }
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);
}
