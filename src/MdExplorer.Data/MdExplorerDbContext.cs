using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data;

/// <summary>
/// Primärer EF-Core-DbContext der Anwendung. Aggregiert alle persistierten
/// Domain-Entitäten und konfiguriert ihre Mappings über die Fluent-API.
/// </summary>
/// <remarks>
/// Ein gescheitertes Speichern verlässt den Kontext als <see cref="DatabaseOperationException"/>
/// statt als <see cref="DbUpdateException"/>: Die Dienste darüber fangen
/// <see cref="System.Data.Common.DbException"/> und kennen EF Core nicht.
/// </remarks>
public class MdExplorerDbContext(DbContextOptions<MdExplorerDbContext> options) : DbContext(options)
{
    private const string SaveFailedMessage = "Speichern in die Datenbank fehlgeschlagen.";

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateException exception)
        {
            throw new DatabaseOperationException(SaveFailedMessage, exception);
        }
    }

    /// <inheritdoc />
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            throw new DatabaseOperationException(SaveFailedMessage, exception);
        }
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);
        _ = modelBuilder.ApplyConfigurationsFromAssembly(typeof(MdExplorerDbContext).Assembly);
    }
}
