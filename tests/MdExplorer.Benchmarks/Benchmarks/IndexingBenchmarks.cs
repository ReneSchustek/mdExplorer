using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using MdExplorer.Benchmarks.Stock;

namespace MdExplorer.Benchmarks.Benchmarks;

/// <summary>
/// Der Hauptweg von Anfang bis Ende: ein leerer Index über den vollen Bestand, wie beim ersten
/// Start oder nach dem Hinzufügen einer großen Wurzel.
/// </summary>
/// <remarks>
/// <para>
/// Ein Durchlauf dauert Sekunden bis Minuten und verändert die Datenbank. Deshalb misst jede
/// Wiederholung genau einen Durchlauf auf einer frisch migrierten Datenbank
/// (<see cref="RunStrategy.Monitoring"/>); das Anlegen liegt außerhalb der Messung. Drei
/// Wiederholungen reichen, um einen Ausreißer zu erkennen, und halten den Lauf in Minuten.
/// </para>
/// <para>
/// Die Notizen liegen nach dem ersten Lauf im Seitencache des Systems. Gemessen wird damit der
/// eigene Code samt SQLite, nicht die Platte; ein Kaltstart nach dem Hochfahren dauert länger.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, iterationCount: 3)]
[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet erzeugt zu jeder Messklasse eine abgeleitete Klasse in einer eigenen Baugruppe und verlangt dafür einen öffentlichen, nicht versiegelten Typ.")]
public class IndexingBenchmarks
{
    private string _databasePath = string.Empty;
    private IndexedStock? _stock;

    /// <summary>Legt den Pfad der Mess-Datenbank fest, getrennt vom zwischengespeicherten Index.</summary>
    [GlobalSetup]
    public void Setup() => _databasePath = Path.Combine(IndexedStock.CacheDirectory, "messlauf.db");

    // BenchmarkDotNet ruft Aufbau und Abbau einer Wiederholung nur synchron auf; eine Task-Methode
    // übersetzt der erzeugte Messrahmen nicht. Das Warten beim Abbau blockiert keinen UI-Thread,
    // der Prozess dient allein der Messung.

    /// <summary>Migriert eine leere Datenbank, außerhalb der Messung.</summary>
    [IterationSetup]
    public void CreateEmptyIndex() => _stock = IndexedStock.CreateEmpty(_databasePath);

    /// <summary>Gibt die Dienste frei und löscht die Mess-Datenbank.</summary>
    [IterationCleanup]
    public void DropIndex()
    {
        if (_stock is not null)
        {
            _stock.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _stock = null;
        }

        IndexedStock.DeleteDatabase(_databasePath);
    }

    /// <summary>Dateien erfassen, parsen und in den Volltextindex schreiben.</summary>
    /// <returns>Anzahl der indizierten Dateien.</returns>
    [Benchmark]
    public async Task<int> FullIndex()
    {
        IndexedStock stock = _stock ?? throw new InvalidOperationException("Die Mess-Datenbank fehlt; der Iterationsaufbau ist nicht gelaufen.");
        int indexed = await stock.IndexAsync(CancellationToken.None).ConfigureAwait(false);
        MeasurementCheck.RequireRows(indexed, nameof(FullIndex));
        return indexed;
    }
}
