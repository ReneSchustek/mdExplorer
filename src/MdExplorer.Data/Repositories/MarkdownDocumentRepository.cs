using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Repositories;

/// <summary>EF-Core-gestützte Implementierung von <see cref="IMarkdownDocumentRepository"/>.</summary>
public sealed class MarkdownDocumentRepository(MdExplorerDbContext dbContext) : IMarkdownDocumentRepository
{
    private readonly MdExplorerDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    /// <inheritdoc />
    public async Task<MarkdownDocument?> GetByMarkdownFileIdAsync(Guid markdownFileId, CancellationToken cancellationToken) =>
        await _dbContext.Set<MarkdownDocument>()
            .AsNoTracking()
            .FirstOrDefaultAsync(document => document.MarkdownFileId == markdownFileId, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, MarkdownDocument>> GetByMarkdownFileIdsAsync(
        IReadOnlyCollection<Guid> markdownFileIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(markdownFileIds);
        if (markdownFileIds.Count == 0)
        {
            return new Dictionary<Guid, MarkdownDocument>();
        }

        Guid[] ids = [.. markdownFileIds];
        Dictionary<Guid, MarkdownDocument> result = new(ids.Length);
        // Getrackt (kein AsNoTracking), denn der Aufrufer mutiert die geladenen Dokumente und ruft
        // anschließend Update/SaveChanges; ein Detached-Ergebnis würde die Änderungen verlieren.
        foreach (Guid[] chunk in ids.Chunk(SqliteInList.BatchSize))
        {
            List<MarkdownDocument> chunkResult = await _dbContext.Set<MarkdownDocument>()
                .Where(document => chunk.Contains(document.MarkdownFileId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (MarkdownDocument document in chunkResult)
            {
                result[document.MarkdownFileId] = document;
            }
        }
        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetStaleOrMissingAsync(
        IReadOnlyDictionary<Guid, string> hashesByFileId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hashesByFileId);
        if (hashesByFileId.Count == 0)
        {
            return [];
        }

        Dictionary<Guid, string> existingHashes = await LoadHashesAsync(hashesByFileId.Keys, cancellationToken).ConfigureAwait(false);
        return
        [
            .. hashesByFileId
                .Where(wanted => !existingHashes.TryGetValue(wanted.Key, out string? existing) || existing != wanted.Value)
                .Select(wanted => wanted.Key),
        ];
    }

    /// <inheritdoc />
    public async Task AddAsync(MarkdownDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        _ = await _dbContext.Set<MarkdownDocument>().AddAsync(document, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Update(MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _ = _dbContext.Set<MarkdownDocument>().Update(document);
    }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    private async Task<Dictionary<Guid, string>> LoadHashesAsync(IEnumerable<Guid> fileIds, CancellationToken cancellationToken)
    {
        Dictionary<Guid, string> hashes = [];
        foreach (Guid[] chunk in fileIds.Chunk(SqliteInList.BatchSize))
        {
            List<(Guid Id, string Hash)> chunkResult = await _dbContext.Set<MarkdownDocument>()
                .AsNoTracking()
                .Where(document => chunk.Contains(document.MarkdownFileId))
                .Select(document => new ValueTuple<Guid, string>(document.MarkdownFileId, document.SourceContentHash))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach ((Guid id, string hash) in chunkResult)
            {
                hashes[id] = hash;
            }
        }
        return hashes;
    }
}
