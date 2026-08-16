using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Repositories;

/// <summary>EF-Core-gestützte Implementierung von <see cref="ITagRepository"/>.</summary>
public sealed class TagRepository(MdExplorerDbContext dbContext) : ITagRepository
{
    /// <summary>
    /// Maximalgröße für SQLite-IN-Listen — gleiche Begründung wie in
    /// <see cref="MarkdownDocumentRepository"/>: defensiv unter dem 999-Parameter-Limit alter SQLite-Builds.
    /// </summary>
    private const int SqliteInListBatchSize = 500;

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
        foreach (string[] chunk in slugArray.Chunk(SqliteInListBatchSize))
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
    public async Task ReplaceFileTagsAsync(Guid markdownFileId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tagIds);

        List<MarkdownFileTag> existing = await _dbContext.Set<MarkdownFileTag>()
            .Where(link => link.MarkdownFileId == markdownFileId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        _dbContext.Set<MarkdownFileTag>().RemoveRange(existing);

        foreach (Guid tagId in tagIds)
        {
            _ = await _dbContext.Set<MarkdownFileTag>().AddAsync(
                new MarkdownFileTag { MarkdownFileId = markdownFileId, TagId = tagId },
                cancellationToken).ConfigureAwait(false);
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
}
