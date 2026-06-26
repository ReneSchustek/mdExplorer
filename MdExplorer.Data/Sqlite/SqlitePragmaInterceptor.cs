using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MdExplorer.Data.Sqlite;

/// <summary>
/// Setzt die SQLite-Pragmas unmittelbar nach Aufbau jeder Verbindung.
/// Aktiviert WAL-Modus, lockert Synchronisation auf <c>NORMAL</c>, setzt
/// einen Lock-Wartepuffer (<c>busy_timeout</c>), hält temporäre Strukturen
/// im RAM, vergrößert den Page-Cache und schaltet Foreign Keys ein.
/// <c>busy_timeout</c> ist Pflicht — ohne ihn meldet SQLite parallele
/// Writer sofort mit <c>SQLITE_BUSY</c>/<c>SQLITE_LOCKED</c> statt zu warten.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    /// <summary>Wartezeit für blockierte SQLite-Schreibzugriffe (ms).</summary>
    public const int BusyTimeoutMilliseconds = 5000;

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ApplyPragmas(connection);
        base.ConnectionOpened(connection, eventData);
    }

    /// <inheritdoc />
    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ApplyPragmas(connection);
        return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private static void ApplyPragmas(DbConnection connection)
    {
        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode = WAL;";
            _ = command.ExecuteNonQuery();
        }
        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA synchronous = NORMAL;";
            _ = command.ExecuteNonQuery();
        }
        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA busy_timeout = 5000;";
            _ = command.ExecuteNonQuery();
        }
        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA temp_store = MEMORY;";
            _ = command.ExecuteNonQuery();
        }
        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA cache_size = -20000;";
            _ = command.ExecuteNonQuery();
        }
        using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys = ON;";
            _ = command.ExecuteNonQuery();
        }
    }
}
