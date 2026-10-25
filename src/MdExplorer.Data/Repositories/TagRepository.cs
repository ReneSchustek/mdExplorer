using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Repositories;

/// <summary>EF-Core-gestützte Implementierung von <see cref="ITagRepository"/>.</summary>
public sealed class TagRepository(MdExplorerDbContext dbContext) : ITagRepository
{
    private readonly MdExplorerDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    /// <inheritdoc />
    public async Task<IReadOnlyList<Tag>> GetBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slugs);
        if (slugs.Count == 0)
        {
            return [];
        }

        string[] slugArray = [.. slugs];
        List<Tag> result = new(slugArray.Length);
        foreach (string[] chunk in slugArray.Chunk(SqliteInList.BatchSize))
        {
            List<Tag> chunkResult = await _dbContext.Set<Tag>()
                .AsNoTracking()
                .Where(tag => chunk.Contains(tag.Slug))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            result.AddRange(chunkResult);
        }
        return result;
    }

    /// <inheritdoc />
    public async Task AddAsync(Tag tag, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tag);
        _ = await _dbContext.Set<Tag>().AddAsync(tag, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReplaceFileTagsAsync(
        IReadOnlyDictionary<Guid, IReadOnlyCollection<Guid>> tagIdsByFile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tagIdsByFile);
        if (tagIdsByFile.Count == 0)
        {
            return;
        }

        ILookup<Guid, MarkdownFileTag> existing = await LoadLinksAsync([.. tagIdsByFile.Keys], cancellationToken).ConfigureAwait(false);
        foreach ((Guid markdownFileId, IReadOnlyCollection<Guid> tagIds) in tagIdsByFile)
        {
            ApplyDifference(markdownFileId, existing[markdownFileId], [.. tagIds]);
        }
    }

    /// <inheritdoc />
    public Task<int> RemoveOrphanedTagsAsync(CancellationToken cancellationToken)
    {
        // Als Anweisung, nicht über den Änderungsverfolger: Es geht um eine Aufräumfrage über
        // die ganze Tabelle, nicht um einzelne bekannte Zeilen.
        return _dbContext.Set<Tag>()
            .Where(tag => !_dbContext.Set<MarkdownFileTag>().Any(link => link.TagId == tag.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    // Getrackt geladen: Was nicht mehr gewünscht ist, wird über den Änderungsverfolger entfernt.
    private async Task<ILookup<Guid, MarkdownFileTag>> LoadLinksAsync(Guid[] markdownFileIds, CancellationToken cancellationToken)
    {
        List<MarkdownFileTag> links = [];
        foreach (Guid[] chunk in markdownFileIds.Chunk(SqliteInList.BatchSize))
        {
            links.AddRange(await _dbContext.Set<MarkdownFileTag>()
                .Where(link => chunk.Contains(link.MarkdownFileId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false));
        }

        return links.ToLookup(link => link.MarkdownFileId);
    }

    // Nur die Differenz: Bleibende Zuordnungen bleiben unberührt, statt entfernt und gleich wieder
    // angelegt zu werden. Das spart Schreibvorgänge beim häufigsten Fall, dem Neu-Parsen mit
    // unveränderten Schlagworten.
    private void ApplyDifference(Guid markdownFileId, IEnumerable<MarkdownFileTag> current, HashSet<Guid> wanted)
    {
        List<MarkdownFileTag> links = [.. current];
        _dbContext.Set<MarkdownFileTag>().RemoveRange(links.Where(link => !wanted.Contains(link.TagId)));

        wanted.ExceptWith(links.Select(link => link.TagId));
        foreach (Guid tagId in wanted)
        {
            _ = _dbContext.Set<MarkdownFileTag>().Add(new MarkdownFileTag { MarkdownFileId = markdownFileId, TagId = tagId });
        }
    }
}
