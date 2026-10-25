using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MdExplorer.Data.Sqlite;

/// <summary>
/// Setzt die SQLite-Pragmas unmittelbar nach Aufbau jeder Verbindung, die über den
/// <see cref="MdExplorerDbContext"/> geöffnet wird.
/// Aktiviert WAL-Modus, lockert Synchronisation auf <c>NORMAL</c>, setzt
/// einen Lock-Wartepuffer (<c>busy_timeout</c>), hält temporäre Strukturen
/// im RAM, vergrößert den Page-Cache und schaltet Foreign Keys ein.
/// <c>busy_timeout</c> ist Pflicht, denn ohne ihn meldet SQLite parallele
/// Writer sofort mit <c>SQLITE_BUSY</c>/<c>SQLITE_LOCKED</c> statt zu warten.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    /// <summary>
    /// Wartezeit für blockierte SQLite-Schreibzugriffe (ms). So lange wartet eine Verbindung auf
    /// die Sperre eines anderen Schreibers, etwa des Indexers beim Speichern eines Stapels.
    /// Reicht sie nicht, greift danach die <see cref="SqliteRetryingExecutionStrategy"/>.
    /// </summary>
    public const int BusyTimeoutMilliseconds = 5000;

    /// <summary>
    /// Größe des Seiten-Zwischenspeichers; negativ heißt Kibibyte statt Seiten, hier also rund
    /// 20 MB je Verbindung statt der knapp 2 MB, die SQLite von sich aus nimmt.
    /// </summary>
    internal const int PageCacheKibibytes = -20_000;

    /// <summary>
    /// Die Pragmas in der Reihenfolge, in der sie gesetzt werden, als eine konstante Anweisung.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Die Zahlen stehen ausgeschrieben im Text, weil eine Konstante nur aus Konstanten bestehen
    /// kann. Dass sie mit <see cref="BusyTimeoutMilliseconds"/> und <see cref="PageCacheKibibytes"/>
    /// übereinstimmen, liest ein Test aus der Datenbank zurück.
    /// </para>
    /// <para>
    /// <c>synchronous = NORMAL</c> kann bei einem Stromausfall die letzten Schreibvorgänge
    /// kosten. Für diese Datenbank ist das tragbar: Sie ist ein Index, der sich aus den
    /// Markdown-Dateien neu aufbauen lässt. <c>foreign_keys</c> ist in SQLite ab Werk aus; ohne
    /// das Pragma griffe keiner der Cascade-Deletes.
    /// </para>
    /// <para>
    /// <c>mmap_size</c> fehlt bewusst. Jede gepoolte Verbindung bildete dafür bis zu 256 MB der
    /// Datei in den Adressraum ab, und kein Lauf des Messprojekts zeigt die Lesezugriffe als
    /// Engpass; gesetzt wird es erst, wenn eine Messung einen Gewinn belegt.
    /// </para>
    /// </remarks>
    internal const string PragmaSql = """
        PRAGMA journal_mode = WAL;
        PRAGMA synchronous = NORMAL;
        PRAGMA busy_timeout = 5000;
        PRAGMA temp_store = MEMORY;
        PRAGMA cache_size = -20000;
        PRAGMA foreign_keys = ON;
        """;

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
        using DbCommand command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        _ = command.ExecuteNonQuery();
    }
}
