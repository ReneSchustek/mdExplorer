using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Repositories;

/// <summary>
/// EF-Core-Implementierung von <see cref="ISearchSourceProvider"/>. Lädt Dokument-Stammdaten
/// und Tag-Aggregat in zwei untracked Queries — der Snapshot ist ein Read-Only-View.
/// </summary>
public sealed class SearchSourceProvider : ISearchSourceProvider
{
    private readonly MdExplorerDbContext _dbContext;

    /// <summary>Erzeugt den Provider und injiziert den DbContext.</summary>
    public SearchSourceProvider(MdExplorerDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<SearchSourceData> LoadAsync(CancellationToken cancellationToken)
    {
        List<SearchSourceDocument> documents = await _dbContext.Set<MarkdownDocument>()
            .AsNoTracking()
            .Join(
                _dbContext.Set<MarkdownFile>().AsNoTracking(),
                document => document.MarkdownFileId,
                file => file.Id,
                (document, file) => new SearchSourceDocument(
                    document.MarkdownFileId,
                    file.FileNameWithoutExtension,
                    file.AbsolutePath,
                    file.RelativePath,
                    document.SourceContentHash,
                    document.FrontmatterJson))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        documents.Sort(static (left, right) => left.MarkdownFileId.CompareTo(right.MarkdownFileId));

        List<TagJoinRow> tagRows = await _dbContext.Set<MarkdownFileTag>()
            .AsNoTracking()
            .Join(
                _dbContext.Set<Tag>().AsNoTracking(),
                link => link.TagId,
                tag => tag.Id,
                (link, tag) => new TagJoinRow(link.MarkdownFileId, tag.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, List<string>> tagBuffers = [];
        foreach (TagJoinRow row in tagRows)
        {
            if (!tagBuffers.TryGetValue(row.MarkdownFileId, out List<string>? names))
            {
                names = [];
                tagBuffers[row.MarkdownFileId] = names;
            }
            names.Add(row.Name);
        }

        Dictionary<Guid, IReadOnlyList<string>> tagsByFileId = new(tagBuffers.Count);
        foreach (KeyValuePair<Guid, List<string>> entry in tagBuffers)
        {
            tagsByFileId[entry.Key] = entry.Value;
        }

        return new SearchSourceData(documents, tagsByFileId);
    }

    private sealed record TagJoinRow(Guid MarkdownFileId, string Name);
}
