using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Data;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Parser.Tests.Fakes;

/// <summary>
/// Schlagwort-Repository im Arbeitsspeicher, das sich beim Speichern wie EF mit SQLite verhält:
/// <see cref="AddAsync"/> merkt nur vor, erst <see cref="SaveChangesAsync"/> schreibt. Bis dahin
/// sieht <c>GetBySlugsAsync</c> nur den gespeicherten Stand, und ein doppelter Slug scheitert wie
/// der eindeutige Index (UNIQUE constraint failed: Tags.Slug).
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

    public Task ReplaceFileTagsAsync(IReadOnlyDictionary<Guid, IReadOnlyCollection<Guid>> tagIdsByFile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tagIdsByFile);
        foreach ((Guid markdownFileId, IReadOnlyCollection<Guid> tagIds) in tagIdsByFile)
        {
            _fileTagLinks[markdownFileId] = [.. tagIds.Distinct()];
        }

        return Task.CompletedTask;
    }

    public Task<int> RemoveOrphanedTagsAsync(CancellationToken cancellationToken)
    {
        HashSet<Guid> linked = [];
        foreach (List<Guid> tagIds in _fileTagLinks.Values)
        {
            linked.UnionWith(tagIds);
        }

        List<string> orphans = [.. _tagsBySlug
            .Where(entry => !linked.Contains(entry.Value.Id))
            .Select(entry => entry.Key)];

        foreach (string slug in orphans)
        {
            _ = _tagsBySlug.Remove(slug);
        }

        return Task.FromResult(orphans.Count);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        HashSet<string> pendingSlugs = new(StringComparer.Ordinal);
        foreach (Tag pending in _pendingTagAdds)
        {
            if (!pendingSlugs.Add(pending.Slug))
            {
                throw UniqueViolation($"UNIQUE constraint failed: Tags.Slug (duplicate '{pending.Slug}' in pending inserts).");
            }
            if (_tagsBySlug.ContainsKey(pending.Slug))
            {
                throw UniqueViolation($"UNIQUE constraint failed: Tags.Slug (slug '{pending.Slug}' already committed).");
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

    // So meldet das echte Repository den Verstoß: EF Core wirft eine DbUpdateException, der
    // Datenkontext reicht sie als DatabaseOperationException weiter.
    private static DatabaseOperationException UniqueViolation(string message) =>
        new(message, new DbUpdateException(message));
}
