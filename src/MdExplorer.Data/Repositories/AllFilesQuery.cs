using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Repositories;

/// <summary>
/// EF-Core-Implementierung von <see cref="IAllFilesQuery"/>. Holt alle indizierten Dateien
/// und joint die zugehoerigen Tag-Slugs in zwei untracked Queries (keine N+1, kein
/// Cartesian-Product). Tag-Slugs werden in der ersten DB-Antwort projiziert, anschliessend
/// pro Datei zugeordnet — vermeidet das LEFT-JOIN-Explosionsproblem bei mehreren Tags pro Datei.
/// </summary>
public sealed class AllFilesQuery : IAllFilesQuery
{
    private readonly MdExplorerDbContext _dbContext;

    /// <summary>Erzeugt die Query und injiziert den DbContext.</summary>
    public AllFilesQuery(MdExplorerDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AllFilesRow>> GetAllAsync(CancellationToken cancellationToken)
    {
        IQueryable<MarkdownFile> files = _dbContext.Set<MarkdownFile>().AsNoTracking();
        IQueryable<MarkdownFileTag> links = _dbContext.Set<MarkdownFileTag>().AsNoTracking();
        IQueryable<Tag> tags = _dbContext.Set<Tag>().AsNoTracking();

        List<FileRowProjection> rawFiles = await files
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.RelativePath)
            .Select(file => new FileRowProjection(
                file.Id,
                file.FileNameWithoutExtension,
                file.RelativePath,
                file.AbsolutePath,
                file.LastWriteTimeUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rawFiles.Count == 0)
        {
            return [];
        }

        IQueryable<TagSlugProjection> tagQuery =
            from link in links
            join tag in tags on link.TagId equals tag.Id
            orderby tag.Slug
            select new TagSlugProjection(link.MarkdownFileId, tag.Slug);

        List<TagSlugProjection> rawTags = await tagQuery
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, List<string>> slugsByFile = new(rawFiles.Count);
        foreach (TagSlugProjection projection in rawTags)
        {
            if (!slugsByFile.TryGetValue(projection.FileId, out List<string>? bucket))
            {
                bucket = [];
                slugsByFile[projection.FileId] = bucket;
            }
            bucket.Add(projection.Slug);
        }

        List<AllFilesRow> result = new(rawFiles.Count);
        foreach (FileRowProjection file in rawFiles)
        {
            IReadOnlyList<string> slugs = slugsByFile.TryGetValue(file.Id, out List<string>? bucket) ? bucket : [];
            result.Add(new AllFilesRow(
                file.Id,
                file.FileNameWithoutExtension,
                file.RelativePath,
                file.AbsolutePath,
                file.LastWriteTimeUtc,
                slugs));
        }
        return result;
    }

    private sealed record FileRowProjection(
        Guid Id,
        string FileNameWithoutExtension,
        string RelativePath,
        string AbsolutePath,
        DateTime LastWriteTimeUtc);

    private sealed record TagSlugProjection(Guid FileId, string Slug);
}
