using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace MdExplorer.Data.Repositories;

/// <summary>
/// EF-Core-gestützte Implementierung von <see cref="IMarkdownFileRepository"/>.
/// Lebt in der Data-Schicht, damit das Indexer-Modul EF-Core-frei bleibt.
/// </summary>
public sealed class MarkdownFileRepository(MdExplorerDbContext dbContext) : IMarkdownFileRepository
{
    private readonly MdExplorerDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    /// <inheritdoc />
    public async Task<MarkdownFile?> GetByAbsolutePathAsync(string absolutePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        return await _dbContext.Set<MarkdownFile>()
            .FirstOrDefaultAsync(file => file.AbsolutePath == absolutePath, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<MarkdownFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return null;
        }
        return await _dbContext.Set<MarkdownFile>()
            .AsNoTracking()
            .FirstOrDefaultAsync(file => file.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarkdownFile>> GetAllUnderRootAsync(string rootAbsolutePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootAbsolutePath);
        // Trailing-Separator-Terminator verhindert, dass z.B. der Root "C:\Notes" auch Dateien
        // unter "C:\Notes-evil\..." matcht — beide haetten sonst gleiche StartsWith-Prefix-Praefixe.
        string trimmedRoot = rootAbsolutePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string prefix = trimmedRoot + Path.DirectorySeparatorChar;
        // AsNoTracking: das Repo liefert Read-Only-Sichten — falls Remove(entity) noetig wird,
        // haengt der Caller die Entitaet per Update/Remove an. Spart Change-Tracker-Overhead
        // bei Bulk-Scans (Initial-Indexer-Lauf, Background-Re-Sync).
        List<MarkdownFile> result = await _dbContext.Set<MarkdownFile>()
            .AsNoTracking()
            .Where(file => file.AbsolutePath.StartsWith(prefix))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async Task<Guid?> FindIdByFileNameAsync(string fileNameWithoutExtension, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileNameWithoutExtension);
        Guid match = await _dbContext.Set<MarkdownFile>()
            .Where(file => EF.Functions.Collate(file.FileNameWithoutExtension, "NOCASE") == fileNameWithoutExtension)
            .OrderBy(file => file.RelativePath)
            .Select(file => file.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return match == Guid.Empty ? null : match;
    }

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken cancellationToken) =>
        _dbContext.Set<MarkdownFile>().CountAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(MarkdownFile entity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _ = await _dbContext.Set<MarkdownFile>().AddAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Update(MarkdownFile entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _ = _dbContext.Set<MarkdownFile>().Update(entity);
    }

    /// <inheritdoc />
    public void Remove(MarkdownFile entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        // GetAllUnderRootAsync liefert AsNoTracking-Entities — die muessen vor Remove
        // an den Change-Tracker angehaengt werden. Bevorzugt eine bereits getrackte Kopie
        // mit derselben Id (sonst wirft Attach IdentityConflict, wenn der Caller die Entity
        // im selben Scope schon angelegt/geladen hat).
        MarkdownFile? alreadyTracked = _dbContext.Set<MarkdownFile>().Local.FirstOrDefault(file => file.Id == entity.Id);
        if (alreadyTracked is not null)
        {
            _ = _dbContext.Set<MarkdownFile>().Remove(alreadyTracked);
            return;
        }

        EntityEntry<MarkdownFile> entry = _dbContext.Entry(entity);
        if (entry.State == EntityState.Detached)
        {
            _ = _dbContext.Set<MarkdownFile>().Attach(entity);
        }
        _ = _dbContext.Set<MarkdownFile>().Remove(entity);
    }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
