using System.Data.Common;
using System.Globalization;
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

    /// <summary>Größe des Seiten-Zwischenspeichers; negativ heißt Kibibyte statt Seiten.</summary>
    private const int PageCacheKibibytes = -20_000;

    /// <summary>
    /// Die Pragmas in der Reihenfolge, in der sie gesetzt werden.
    /// </summary>
    /// <remarks>
    /// Als Liste und nicht als sechs gleiche Blöcke: Der nächste Eintrag ist dann eine Zeile
    /// statt sieben. Die Wartezeit kommt aus <see cref="BusyTimeoutMilliseconds"/> — sie stand
    /// bis zum 16.08.2026 ein zweites Mal im Text des Befehls, und wer die Konstante änderte,
    /// änderte nichts.
    /// </remarks>
    private static readonly string[] PragmaStatements =
    [
        "PRAGMA journal_mode = WAL;",
        "PRAGMA synchronous = NORMAL;",
        string.Create(CultureInfo.InvariantCulture, $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds};"),
        "PRAGMA temp_store = MEMORY;",
        string.Create(CultureInfo.InvariantCulture, $"PRAGMA cache_size = {PageCacheKibibytes};"),
        "PRAGMA foreign_keys = ON;",
    ];

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
        foreach (string statement in PragmaStatements)
        {
            using DbCommand command = connection.CreateCommand();
            // CA2100: Der Text stammt ausschließlich aus der obigen Liste — kein Wert von
            // außen, keine Verkettung zur Laufzeit.
#pragma warning disable CA2100
            command.CommandText = statement;
#pragma warning restore CA2100
            _ = command.ExecuteNonQuery();
        }
    }
}
