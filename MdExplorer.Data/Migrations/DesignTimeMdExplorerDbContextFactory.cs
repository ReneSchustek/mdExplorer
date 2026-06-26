using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MdExplorer.Data.Migrations;

/// <summary>
/// Wird ausschließlich von <c>dotnet ef</c> zur Design-Zeit verwendet, um Migrationen
/// zu erzeugen, ohne die App-Schicht zu starten. Zur Laufzeit wird der DbContext
/// vom DI-Container (<see cref="DataServiceCollectionExtensions"/>) konfiguriert.
/// </summary>
public sealed class DesignTimeMdExplorerDbContextFactory : IDesignTimeDbContextFactory<MdExplorerDbContext>
{
    /// <inheritdoc />
    public MdExplorerDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<MdExplorerDbContext> optionsBuilder = new();
        _ = optionsBuilder.UseSqlite(
            "Data Source=design-time.db",
            sqlite => sqlite.MigrationsAssembly(typeof(DesignTimeMdExplorerDbContextFactory).Assembly.FullName));

        return new MdExplorerDbContext(optionsBuilder.Options);
    }
}
