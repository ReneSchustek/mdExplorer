using MdExplorer.Core.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Data.Migrations;

/// <summary>
/// Wendet ausstehende EF-Core-Migrationen idempotent auf den <see cref="MdExplorerDbContext"/> an.
/// </summary>
public sealed partial class DatabaseMigrator(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseMigrator> logger) : IDatabaseMigrator
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    private readonly ILogger<DatabaseMigrator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        MdExplorerDbContext dbContext = scope.ServiceProvider.GetRequiredService<MdExplorerDbContext>();

        IEnumerable<string> pending = await dbContext.Database
            .GetPendingMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);

        List<string> pendingList = [.. pending];
        if (pendingList.Count == 0)
        {
            LogNoPendingMigrations(_logger);
            return;
        }

        LogApplyingMigrations(_logger, pendingList.Count);
        foreach (string migration in pendingList)
        {
            LogPendingMigration(_logger, migration);
        }

        await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        LogMigrationCompleted(_logger);
    }

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Keine ausstehenden Migrationen — Datenbank ist aktuell.")]
    private static partial void LogNoPendingMigrations(ILogger logger);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "Wende {Count} ausstehende Migration(en) an.")]
    private static partial void LogApplyingMigrations(ILogger logger, int count);

    [LoggerMessage(EventId = 12, Level = LogLevel.Debug, Message = "Ausstehende Migration: {Name}")]
    private static partial void LogPendingMigration(ILogger logger, string name);

    [LoggerMessage(EventId = 13, Level = LogLevel.Information, Message = "Migration abgeschlossen.")]
    private static partial void LogMigrationCompleted(ILogger logger);
}
