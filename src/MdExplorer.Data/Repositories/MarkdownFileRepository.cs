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
    /// <summary>
    /// Wie viele Schlüssel höchstens in eine Löschanweisung gehen.
    /// </summary>
    /// <remarks>
    /// SQLite lässt je Anweisung eine begrenzte Zahl gebundener Werte zu — voreingestellt
    /// 32.766. 500 hält deutlichen Abstand dazu und macht jede Portion für sich schnell genug,
    /// dass ein Abbruch nur wenig Arbeit kostet.
    /// </remarks>
    private const int DeleteChunkSize = 500;

    private readonly MdExplorerDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    /// <inheritdoc />
    public async Task<MarkdownFile?> GetByAbsolutePathAsync(string absolutePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        // Der Vergleich ist case-insensitiv, weil die Spalte NOCASE-Collation trägt — die
        // Abfrage nutzt weiterhin den Unique-Index (kein Full-Scan, kein EF.Functions.Collate).
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
        // unter "C:\Notes-evil\..." matcht — beide hätten sonst gleiche StartsWith-Prefix-Präfixe.
        string trimmedRoot = rootAbsolutePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string prefix = trimmedRoot + Path.DirectorySeparatorChar;
        // AsNoTracking: das Repo liefert Read-Only-Sichten — falls Remove(entity) nötig wird,
        // hängt der Caller die Entität per Update/Remove an. Spart Change-Tracker-Overhead
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
        // GetAllUnderRootAsync liefert AsNoTracking-Entities — die müssen vor Remove
        // an den Change-Tracker angehängt werden. Bevorzugt eine bereits getrackte Kopie
        // mit derselben Id (sonst wirft Attach IdentityConflict, wenn der Caller die Entity
        // im selben Scope schon angelegt/geladen hat).
        //
        // FindEntry statt einer Suche über Local: Die Suche lief die Liste der bereits
        // vorgemerkten Entitäten jedes Mal von vorne durch. Bei einem Aufräumdurchgang mit n
        // Entfernungen sind das n²/2 Vergleiche — am 16.08.2026 blieb ein Lauf über 27.000
        // verwaiste Einträge deshalb 20 Minuten lang bei voller Rechenlast stehen, ohne eine
        // Zeile zu schreiben. FindEntry greift auf die Identitätstabelle zu und braucht
        // dafür konstante Zeit.
        MarkdownFile? alreadyTracked = _dbContext.Set<MarkdownFile>().Local.FindEntry(entity.Id)?.Entity;
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
    public async Task<int> RemoveRangeAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return 0;
        }

        int removed = 0;
        foreach (Guid[] portion in ids.Chunk(DeleteChunkSize))
        {
            // Bewusst ohne umschließende Transaktion: Die Wiederholungsstrategie darf jede
            // Portion für sich noch einmal versuchen, und ein abgebrochener Durchgang
            // hinterlässt einen kleineren, aber stimmigen Index statt eines halben.
            removed += await _dbContext.Set<MarkdownFile>()
                .Where(file => portion.Contains(file.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        // Was der Aufrufer noch verfolgt, zeigt jetzt auf Zeilen, die es nicht mehr gibt.
        _dbContext.ChangeTracker.Clear();
        return removed;
    }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
