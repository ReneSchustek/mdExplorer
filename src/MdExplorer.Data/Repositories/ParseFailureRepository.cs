using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Repositories;

/// <summary>EF-Core-gestützte Implementierung von <see cref="IParseFailureRepository"/>.</summary>
public sealed class ParseFailureRepository(MdExplorerDbContext dbContext) : IParseFailureRepository
{
    private readonly MdExplorerDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, ParseFailure>> GetByMarkdownFileIdsAsync(
        IReadOnlyCollection<Guid> markdownFileIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(markdownFileIds);
        if (markdownFileIds.Count == 0)
        {
            return new Dictionary<Guid, ParseFailure>();
        }

        Guid[] ids = [.. markdownFileIds];
        Dictionary<Guid, ParseFailure> result = new(ids.Length);
        // Getrackt geladen: RecordAsync findet diese Instanzen danach im Kontext, ohne die Datenbank
        // noch einmal zu fragen, und RemoveAsync bekommt über die Identitätsauflösung genau sie
        // zurück; ein zweites Objekt für dieselbe Zeile entsteht so nicht.
        foreach (Guid[] chunk in ids.Chunk(SqliteInList.BatchSize))
        {
            List<ParseFailure> chunkResult = await _dbContext.Set<ParseFailure>()
                .Where(failure => chunk.Contains(failure.MarkdownFileId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (ParseFailure failure in chunkResult)
            {
                result[failure.MarkdownFileId] = failure;
            }
        }
        return result;
    }

    /// <inheritdoc />
    public async Task RecordAsync(ParseFailure failure, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(failure);

        // Erst unter den schon verfolgten Vermerken: Der Parser lädt sie je Portion vorab, und eine
        // Abfrage je gescheiterter Datei wäre ein N+1. Die Datenbank fragt nur, wer nicht vorab lud.
        ParseFailure? existing = _dbContext.Set<ParseFailure>().Local
            .FirstOrDefault(candidate => candidate.MarkdownFileId == failure.MarkdownFileId)
            ?? await _dbContext.Set<ParseFailure>()
                .FirstOrDefaultAsync(candidate => candidate.MarkdownFileId == failure.MarkdownFileId, cancellationToken)
                .ConfigureAwait(false);

        if (existing is null)
        {
            _ = await _dbContext.Set<ParseFailure>().AddAsync(failure, cancellationToken).ConfigureAwait(false);
            return;
        }

        existing.ContentHash = failure.ContentHash;
        existing.EngineVersion = failure.EngineVersion;
        existing.FailureReason = failure.FailureReason;
        existing.FailedAtUtc = failure.FailedAtUtc;
    }

    /// <inheritdoc />
    public async Task RemoveAsync(IReadOnlyCollection<Guid> markdownFileIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(markdownFileIds);
        if (markdownFileIds.Count == 0)
        {
            return;
        }

        Guid[] ids = [.. markdownFileIds];
        foreach (Guid[] chunk in ids.Chunk(SqliteInList.BatchSize))
        {
            List<ParseFailure> chunkResult = await _dbContext.Set<ParseFailure>()
                .Where(failure => chunk.Contains(failure.MarkdownFileId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (chunkResult.Count > 0)
            {
                _dbContext.Set<ParseFailure>().RemoveRange(chunkResult);
            }
        }
    }

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken cancellationToken) =>
        _dbContext.Set<ParseFailure>().AsNoTracking().CountAsync(cancellationToken);

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
