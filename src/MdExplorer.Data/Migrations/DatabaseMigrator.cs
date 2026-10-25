using System.Data.Common;
using System.IO;
using MdExplorer.Core.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Data.Migrations;

/// <summary>
/// Wendet ausstehende EF-Core-Migrationen idempotent auf den <see cref="MdExplorerDbContext"/> an,
/// sichert eine bestehende Datenbank vorher und lässt SQLite danach seine Statistiken auffrischen.
/// </summary>
public sealed partial class DatabaseMigrator(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseMigrator> logger) : IDatabaseMigrator
{
    /// <summary>Endung der Sicherung neben der Datenbankdatei.</summary>
    internal const string BackupSuffix = ".before-migration.db";

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    private readonly ILogger<DatabaseMigrator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        MdExplorerDbContext dbContext = scope.ServiceProvider.GetRequiredService<MdExplorerDbContext>();

        List<string> pending = [.. await dbContext.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)];
        if (pending.Count == 0)
        {
            LogNoPendingMigrations(_logger);
        }
        else
        {
            await BackUpExistingDatabaseAsync(dbContext, cancellationToken).ConfigureAwait(false);
            await ApplyAsync(dbContext, pending, cancellationToken).ConfigureAwait(false);
        }

        await OptimizeAsync(dbContext, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Keine ausstehenden Migrationen — Datenbank ist aktuell.")]
    private static partial void LogNoPendingMigrations(ILogger logger);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "Wende {Count} ausstehende Migration(en) an.")]
    private static partial void LogApplyingMigrations(ILogger logger, int count);

    [LoggerMessage(EventId = 12, Level = LogLevel.Debug, Message = "Ausstehende Migration: {Name}")]
    private static partial void LogPendingMigration(ILogger logger, string name);

    [LoggerMessage(EventId = 13, Level = LogLevel.Information, Message = "Migration abgeschlossen.")]
    private static partial void LogMigrationCompleted(ILogger logger);

    [LoggerMessage(EventId = 14, Level = LogLevel.Information, Message = "Datenbank vor der Migration gesichert: {BackupPath}")]
    private static partial void LogBackupWritten(ILogger logger, string backupPath);

    [LoggerMessage(EventId = 15, Level = LogLevel.Warning, Message = "Sicherung vor der Migration fehlgeschlagen ({BackupPath}); die Migration läuft ohne Sicherung.")]
    private static partial void LogBackupFailed(ILogger logger, Exception exception, string backupPath);

    [LoggerMessage(EventId = 16, Level = LogLevel.Warning, Message = "PRAGMA optimize nach dem Start fehlgeschlagen.")]
    private static partial void LogOptimizeFailed(ILogger logger, Exception exception);

    private async Task ApplyAsync(MdExplorerDbContext dbContext, List<string> pending, CancellationToken cancellationToken)
    {
        LogApplyingMigrations(_logger, pending.Count);
        foreach (string migration in pending)
        {
            LogPendingMigration(_logger, migration);
        }

        await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        LogMigrationCompleted(_logger);
    }

    /// <summary>
    /// Legt vor einer Migration eine Kopie der bestehenden Datenbank neben ihr ab.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Eine Migration baut Tabellen neu auf und löscht im Zweifel Zeilen. Geht dabei etwas schief,
    /// bleibt mit der Kopie der Stand davor. <c>VACUUM INTO</c> statt einer Dateikopie: Im WAL-Modus
    /// stehen jüngste Änderungen noch in der <c>-wal</c>-Datei, eine Kopie der Hauptdatei allein
    /// wäre unvollständig. Gehalten wird nur die letzte Sicherung, damit die Kopien nicht anwachsen.
    /// </para>
    /// <para>
    /// Scheitert die Sicherung, läuft die Migration trotzdem: Die Datenbank ist ein Index, der sich
    /// aus den Markdown-Dateien neu aufbauen lässt; eine Anwendung, die ohne Sicherung gar nicht mehr
    /// startet, wäre der größere Schaden. Der Fehler steht im Protokoll.
    /// </para>
    /// </remarks>
    private async Task BackUpExistingDatabaseAsync(MdExplorerDbContext dbContext, CancellationToken cancellationToken)
    {
        string databasePath = dbContext.Database.GetDbConnection().DataSource;
        bool hasHistory = (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).Any();
        if (!hasHistory || string.IsNullOrEmpty(databasePath) || !File.Exists(databasePath))
        {
            return;
        }

        string backupPath = databasePath + BackupSuffix;
        try
        {
            File.Delete(backupPath);
            _ = await dbContext.Database
                .ExecuteSqlAsync($"VACUUM INTO {backupPath}", cancellationToken)
                .ConfigureAwait(false);
            LogBackupWritten(_logger, backupPath);
        }
        catch (Exception exception) when (exception is DbException or IOException or UnauthorizedAccessException)
        {
            LogBackupFailed(_logger, exception, backupPath);
        }
    }

    /// <summary>
    /// Lässt SQLite nach dem Start seine Abfragestatistiken auffrischen.
    /// </summary>
    /// <remarks>
    /// <c>PRAGMA optimize</c> untersucht nur Tabellen, bei denen sich das lohnt, und kostet sonst fast
    /// nichts (am Messbestand mit 7.700 Notizen und 83 MB: 1 bis 17 ms, mit erzwungener Analyse 26 ms);
    /// SQLite empfiehlt den Aufruf beim Start oder Ende einer langen Verbindung. Ein voller
    /// <c>VACUUM</c> unterbleibt bewusst: Er schreibt die ganze Datei neu und sperrt den Indexer, und
    /// frei gewordene Seiten verwendet SQLite ohnehin für neue Daten. Der Volltextindex fügt seine
    /// Teilstücke selbst zusammen (FTS5-Voreinstellung <c>automerge</c>); ein eigenes
    /// <c>optimize</c> der FTS5-Tabelle bräuchte es erst, wenn eine Messung Zerfall zeigt.
    /// </remarks>
    private async Task OptimizeAsync(MdExplorerDbContext dbContext, CancellationToken cancellationToken)
    {
        try
        {
            _ = await dbContext.Database.ExecuteSqlRawAsync("PRAGMA optimize;", cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception)
        {
            // Eine verpasste Statistik verlangsamt höchstens Abfragen; der Start darf daran nicht scheitern.
            LogOptimizeFailed(_logger, exception);
        }
    }
}
