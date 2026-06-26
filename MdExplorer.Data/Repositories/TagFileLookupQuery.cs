using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Repositories;

/// <summary>
/// EF-Core-Implementierung von <see cref="ITagFileLookupQuery"/>. Vermeidet Change-Tracking
/// per <see cref="EntityFrameworkQueryableExtensions.AsNoTracking{TEntity}"/> und nutzt den
/// B-Tree-Index auf <c>MarkdownFileTags.TagId</c>. Liefert in einem JOIN-Schritt
/// — kein N+1.
/// </summary>
public sealed class TagFileLookupQuery : ITagFileLookupQuery
{
    private readonly MdExplorerDbContext _dbContext;

    /// <summary>Erzeugt die Query und injiziert den DbContext.</summary>
    public TagFileLookupQuery(MdExplorerDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TagFileLookupRow>> GetFilesByTagSlugAsync(string slug, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        IQueryable<MarkdownFileTag> links = _dbContext.Set<MarkdownFileTag>().AsNoTracking();
        IQueryable<Tag> tags = _dbContext.Set<Tag>().AsNoTracking();
        IQueryable<MarkdownFile> files = _dbContext.Set<MarkdownFile>().AsNoTracking();

        IQueryable<TagFileLookupRow> query =
            from link in links
            join tag in tags on link.TagId equals tag.Id
            join file in files on link.MarkdownFileId equals file.Id
            where tag.Slug == slug
            orderby file.RelativePath
            select new TagFileLookupRow(file.Id, file.AbsolutePath, file.RelativePath);

        List<TagFileLookupRow> result = await query
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return result;
    }
}
