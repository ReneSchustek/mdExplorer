using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>
/// Dateibestand im Speicher. Die Namenssuche ignoriert wie das Repository die Groß- und
/// Kleinschreibung und nimmt bei gleichem Namen den kleinsten relativen Pfad.
/// </summary>
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

    /// <summary>Der Fehler, den das Lesen per Kennung meldet; <see langword="null"/>, wenn es gelingen soll.</summary>
    public Exception? FailOnGet { get; set; }

    /// <summary>Der Fehler, den das Speichern meldet; <see langword="null"/>, wenn es gelingen soll.</summary>
    /// <remarks>Eine belegte Datenbank meldet sich erst beim Speichern, nicht beim Ändern des Eintrags.</remarks>
    public Exception? FailOnSave { get; set; }

    public Task<MarkdownFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        FailOnGet is not null
            ? Task.FromException<MarkdownFile?>(FailOnGet)
            : Task.FromResult(_store.TryGetValue(id, out MarkdownFile? file) ? file : null);

    public Task<IReadOnlyList<MarkdownFile>> GetAllUnderRootAsync(string rootAbsolutePath, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MarkdownFile>>([.. _store.Values]);

    public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(_store.Count);

    public Task AddAsync(MarkdownFile entity, CancellationToken cancellationToken) { Add(entity); return Task.CompletedTask; }
    public void Update(MarkdownFile entity) => Add(entity);
    public void Remove(MarkdownFile entity) { ArgumentNullException.ThrowIfNull(entity); _ = _store.Remove(entity.Id); }
    public Task<int> RemoveRangeAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        int removed = ids.Count(id => _store.Remove(id));
        return Task.FromResult(removed);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        FailOnSave is not null ? Task.FromException<int>(FailOnSave) : Task.FromResult(0);
}
