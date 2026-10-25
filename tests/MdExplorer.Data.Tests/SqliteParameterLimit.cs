using Microsoft.Data.Sqlite;

namespace MdExplorer.Data.Tests;

/// <summary>
/// Setzt eine Test-Verbindung auf die Parametergrenze, die SQLite vor 3.32 hatte.
/// </summary>
/// <remarks>
/// Die mitgelieferte SQLite erlaubt in der Voreinstellung 32.766 gebundene Parameter. Ein
/// Repository, das eine große Id-Liste ungeteilt als IN-Liste schickt, fiele gegen sie erst ab
/// dieser Menge auf, also in keinem Test. Mit der alten Grenze von 999 bricht eine solche Abfrage
/// schon bei 2.000 Ids mit „too many SQL variables" ab.
/// </remarks>
internal static class SqliteParameterLimit
{
    /// <summary>Parametergrenze von SQLite vor 3.32.</summary>
    public const int Legacy = 999;

    /// <summary>Senkt die Parametergrenze der Verbindung auf <see cref="Legacy"/>.</summary>
    /// <param name="connection">Eine geöffnete Verbindung.</param>
    public static void Restrict(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _ = SQLitePCL.raw.sqlite3_limit(connection.Handle, SQLitePCL.raw.SQLITE_LIMIT_VARIABLE_NUMBER, Legacy);
    }

    /// <summary>Liefert <paramref name="count"/> verschiedene, feste Ids.</summary>
    /// <param name="count">Wie viele Ids.</param>
    /// <returns>Die Ids, aufsteigend von 1 an gezählt.</returns>
    public static List<Guid> DistinctIds(int count) =>
        [.. Enumerable.Range(1, count).Select(index => new Guid(index, 0, 0, new byte[8]))];
}
