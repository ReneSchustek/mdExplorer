using System.Globalization;

namespace MdExplorer.Benchmarks.Benchmarks;

/// <summary>
/// Bricht eine Messung ab, die über nichts gelaufen ist.
/// </summary>
/// <remarks>
/// Eine Abfrage ohne Treffer ist blitzschnell. Eine Messung, die das nicht bemerkt, meldet eine
/// hervorragende Zahl für gar nichts, etwa nach einem Tippfehler im Suchbegriff oder einem Bestand,
/// der nicht indiziert wurde. Deshalb prüft jede Messklasse im Aufbau, dass ihr Vorgang Zeilen liefert.
/// </remarks>
internal static class MeasurementCheck
{
    /// <summary>Wirft, wenn der Vorgang keine Zeilen geliefert hat (<paramref name="rows"/> ist 0).</summary>
    /// <param name="rows">Anzahl gelieferter Zeilen oder Treffer.</param>
    /// <param name="operation">Was gemessen wird, für die Meldung.</param>
    public static void RequireRows(int rows, string operation)
    {
        if (rows > 0)
        {
            return;
        }

        throw new InvalidOperationException(string.Create(
            CultureInfo.InvariantCulture,
            $"„{operation}“ liefert keine Zeilen. Eine Messung darüber sagte nichts; Messbestand und Abfrage prüfen."));
    }
}
