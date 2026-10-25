namespace MdExplorer.Data.Tests;

/// <summary>
/// Liefert fortlaufende, feste Kennungen für Testdaten. Mit Zufallskennungen ließe sich ein
/// Fehler, der nur bei bestimmten Werten auftritt, nicht wiederholen; mit dieser Folge steht
/// in jedem Lauf dieselbe Kennung an derselben Stelle.
/// </summary>
/// <remarks>
/// Der mittlere Teil unterscheidet die Folge von <see cref="SqliteParameterLimit.DistinctIds"/>,
/// damit beide in einem Test nebeneinander stehen können. Je Testklasse eine eigene Instanz:
/// Statischer Zustand vertrüge sich nicht mit den parallel laufenden Testklassen.
/// </remarks>
internal sealed class TestIdSequence
{
    private const short SequenceMarker = 0x7E57;

    private int _last;

    /// <summary>Liefert die nächste Kennung der Folge; sicher auch aus parallelen Aufgaben.</summary>
    /// <returns>Eine neue, in dieser Folge noch nicht vergebene Kennung.</returns>
    public Guid Next() => new(Interlocked.Increment(ref _last), SequenceMarker, 0, new byte[8]);
}
