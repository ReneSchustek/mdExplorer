using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Repositories;

/// <summary>
/// EF-Core-Implementierung von <see cref="ITagStatisticsQuery"/>. Liefert die Top-N-Tags
/// in einer einzigen GROUP-BY-Query (kein N+1) und vermeidet Change-Tracking konsequent
/// über <see cref="EntityFrameworkQueryableExtensions.AsNoTracking{TEntity}"/>.
/// Die Spalten <c>MarkdownFileTags.TagId</c> und
/// <c>MarkdownFiles.LastWriteTimeUtc</c> sind über je einen B-Tree-Index abgedeckt
/// und bilden die kritischen Pfade.
/// </summary>
public sealed class TagStatisticsQuery : ITagStatisticsQuery
{
    private readonly MdExplorerDbContext _dbContext;

    /// <summary>Erzeugt die Query und injiziert den DbContext.</summary>
    public TagStatisticsQuery(MdExplorerDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TagStatisticRow>> GetTopTagsAsync(int topN, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(topN, 1);

        IQueryable<MarkdownFileTag> links = _dbContext.Set<MarkdownFileTag>().AsNoTracking();
        IQueryable<Tag> tags = _dbContext.Set<Tag>().AsNoTracking();
        IQueryable<MarkdownFile> files = _dbContext.Set<MarkdownFile>().AsNoTracking();

        IQueryable<TagStatisticRow> query =
            from link in links
            join tag in tags on link.TagId equals tag.Id
            join file in files on link.MarkdownFileId equals file.Id
            group file by new { tag.Id, tag.Name, tag.Slug } into grouped
            orderby grouped.Count() descending, grouped.Key.Slug ascending
            select new TagStatisticRow(
                grouped.Key.Name,
                grouped.Key.Slug,
                grouped.Count(),
                grouped.Max(file => file.LastWriteTimeUtc));

        List<TagStatisticRow> result = await query
            .Take(topN)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return result;
    }
}
