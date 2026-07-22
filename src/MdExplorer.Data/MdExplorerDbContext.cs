using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data;

/// <summary>
/// Primärer EF-Core-DbContext der Anwendung. Aggregiert alle persistierten
/// Domain-Entitäten und konfiguriert ihre Mappings über die Fluent-API.
/// </summary>
public class MdExplorerDbContext(DbContextOptions<MdExplorerDbContext> options) : DbContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);
        _ = modelBuilder.ApplyConfigurationsFromAssembly(typeof(MdExplorerDbContext).Assembly);
    }
}
