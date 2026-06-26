using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;

namespace MdExplorer.Parser.Tests.Fakes;

/// <summary>
/// EF-realistischer Fake: <see cref="AddAsync"/> puffert in eine Pending-Liste,
/// <see cref="SaveChangesAsync"/> committet sie. Damit reproduziert der Fake das
/// SQLite-Verhalten (UNIQUE constraint failed: Tags.Slug) — der echte EF
/// macht erst beim <c>SaveChanges</c> die Inserts, vorher sieht <c>GetBySlugsAsync</c>
/// nur den committeten DB-Stand.
/// </summary>
internal sealed class FakeTagRepository : ITagRepository
{
    private readonly Dictionary<string, Tag> _tagsBySlug = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, List<Guid>> _fileTagLinks = [];
    private readonly List<Tag> _pendingTagAdds = [];

    public IReadOnlyDictionary<string, Tag> TagsBySlug => _tagsBySlug;

    public IReadOnlyDictionary<Guid, List<Guid>> FileLinks => _fileTagLinks;

    public Task<IReadOnlyList<Tag>> GetBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken)
    {
        List<Tag> result = [];
        foreach (string slug in slugs)
        {
            if (_tagsBySlug.TryGetValue(slug, out Tag? tag))
            {
                result.Add(tag);
            }
        }
        return Task.FromResult<IReadOnlyList<Tag>>(result);
    }

    public Task AddAsync(Tag tag, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tag);
        _pendingTagAdds.Add(tag);
        return Task.CompletedTask;
    }

    public Task ReplaceFileTagsAsync(Guid markdownFileId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken)
    {
        _fileTagLinks[markdownFileId] = [.. tagIds];
        return Task.CompletedTask;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        HashSet<string> pendingSlugs = new(StringComparer.Ordinal);
        foreach (Tag pending in _pendingTagAdds)
        {
            if (!pendingSlugs.Add(pending.Slug))
            {
                throw new InvalidOperationException(
                    $"UNIQUE constraint failed: Tags.Slug (duplicate '{pending.Slug}' in pending inserts).");
            }
            if (_tagsBySlug.ContainsKey(pending.Slug))
            {
                throw new InvalidOperationException(
                    $"UNIQUE constraint failed: Tags.Slug (slug '{pending.Slug}' already committed).");
            }
        }
        foreach (Tag pending in _pendingTagAdds)
        {
            _tagsBySlug[pending.Slug] = pending;
        }
        int added = _pendingTagAdds.Count;
        _pendingTagAdds.Clear();
        return Task.FromResult(added);
    }
}
