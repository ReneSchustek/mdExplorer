using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Repositories;

/// <summary>
/// EF-Core-Implementierung von <see cref="IAllFilesQuery"/>. Holt alle indizierten Dateien
/// und die zugehörigen Tag-Slugs in zwei untracked Queries: erst die Dateien, dann alle
/// Zuordnungen samt Slug, die <see cref="TagsByFile"/> je Datei einsortiert.
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
        List<FileRowProjection> rawFiles = await LoadFilesAsync(cancellationToken).ConfigureAwait(false);
        if (rawFiles.Count == 0)
        {
            return [];
        }

        Dictionary<Guid, IReadOnlyList<string>> slugsByFile = await LoadSlugsByFileAsync(cancellationToken).ConfigureAwait(false);
        return
        [
            .. rawFiles.Select(file => new AllFilesRow(
                file.Id,
                file.FileNameWithoutExtension,
                file.RelativePath,
                file.AbsolutePath,
                file.LastWriteTimeUtc,
                slugsByFile.TryGetValue(file.Id, out IReadOnlyList<string>? slugs) ? slugs : [])),
        ];
    }

    private Task<List<FileRowProjection>> LoadFilesAsync(CancellationToken cancellationToken) =>
        _dbContext.Set<MarkdownFile>()
            .AsNoTracking()
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.RelativePath)
            .Select(file => new FileRowProjection(
                file.Id,
                file.FileNameWithoutExtension,
                file.RelativePath,
                file.AbsolutePath,
                file.LastWriteTimeUtc))
            .ToListAsync(cancellationToken);

    private async Task<Dictionary<Guid, IReadOnlyList<string>>> LoadSlugsByFileAsync(CancellationToken cancellationToken)
    {
        IQueryable<TagSlugProjection> tagQuery =
            from link in _dbContext.Set<MarkdownFileTag>().AsNoTracking()
            join tag in _dbContext.Set<Tag>().AsNoTracking() on link.TagId equals tag.Id
            orderby tag.Slug
            select new TagSlugProjection(link.MarkdownFileId, tag.Slug);

        List<TagSlugProjection> rawTags = await tagQuery.ToListAsync(cancellationToken).ConfigureAwait(false);
        return TagsByFile.Group(rawTags.Select(row => (row.FileId, row.Slug)));
    }

    /// <summary>Die Spalten einer Datei, die die Liste braucht; der Inhalts-Hash bleibt in der Datenbank.</summary>
    private sealed record FileRowProjection(
        Guid Id,
        string FileNameWithoutExtension,
        string RelativePath,
        string AbsolutePath,
        DateTime LastWriteTimeUtc);

    /// <summary>Eine Zuordnung von Datei und Schlagwort, schon auf den Slug verkürzt.</summary>
    private sealed record TagSlugProjection(Guid FileId, string Slug);
}
