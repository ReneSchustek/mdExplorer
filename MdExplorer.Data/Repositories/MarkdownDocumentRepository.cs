using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Repositories;

/// <summary>EF-Core-gestützte Implementierung von <see cref="IMarkdownDocumentRepository"/>.</summary>
public sealed class MarkdownDocumentRepository(MdExplorerDbContext dbContext) : IMarkdownDocumentRepository
{
    /// <summary>
    /// Maximalgroesse fuer SQLite-IN-Listen. Default-Limit fuer gebundene Parameter in SQLite
    /// vor Version 3.32 lag bei 999; wir bleiben defensiv unter dieser Schwelle, damit auch
    /// aeltere Build-Konfigurationen funktionieren.
    /// </summary>
    private const int SqliteInListBatchSize = 500;

    private readonly MdExplorerDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    /// <inheritdoc />
    public async Task<MarkdownDocument?> GetByMarkdownFileIdAsync(Guid markdownFileId, CancellationToken cancellationToken) =>
        await _dbContext.Set<MarkdownDocument>()
            .FirstOrDefaultAsync(document => document.MarkdownFileId == markdownFileId, cancellationToken)
            .ConfigureAwait(false);

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

        Guid[] candidateIds = [.. hashesByFileId.Keys];
        Dictionary<Guid, string> existingHashes = new(candidateIds.Length);
        foreach (Guid[] chunk in candidateIds.Chunk(SqliteInListBatchSize))
        {
            List<(Guid Id, string Hash)> chunkResult = await _dbContext.Set<MarkdownDocument>()
                .AsNoTracking()
                .Where(document => chunk.Contains(document.MarkdownFileId))
                .Select(document => new ValueTuple<Guid, string>(document.MarkdownFileId, document.SourceContentHash))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach ((Guid id, string hash) in chunkResult)
            {
                existingHashes[id] = hash;
            }
        }

        List<Guid> result = [];
        foreach (KeyValuePair<Guid, string> wanted in hashesByFileId)
        {
            if (!existingHashes.TryGetValue(wanted.Key, out string? existing) || existing != wanted.Value)
            {
                result.Add(wanted.Key);
            }
        }
        return result;
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
}
