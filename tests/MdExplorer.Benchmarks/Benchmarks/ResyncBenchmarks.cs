using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using MdExplorer.Benchmarks.Stock;
using MdExplorer.Core.Abstractions;
using MdExplorer.Indexer.Abstractions;
using MdExplorer.Search.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.Benchmarks.Benchmarks;

/// <summary>
/// Der periodische Abgleich über einen unveränderten Bestand.
/// </summary>
/// <remarks>
/// Er läuft im Betrieb in jedem Takt (Vorgabe 300 Sekunden) über alle Dateien, auch wenn sich nichts
/// geändert hat. Gemessen wird, was er dann kostet: eine Abfrage je Wurzel, das Aufzählen der
/// Dateien und der Vergleich von Größe und Änderungszeit im Speicher.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 8)]
[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet erzeugt zu jeder Messklasse eine abgeleitete Klasse in einer eigenen Baugruppe und verlangt dafür einen öffentlichen, nicht versiegelten Typ.")]
public class ResyncBenchmarks
{
    private IndexedStock? _stock;

    // BenchmarkDotNet ruft GlobalSetup vor jeder Messung; fehlt der Bestand, ist das ein Fehler
    // im Aufbau und keine Messung wert.
    private IndexedStock Stock => _stock ?? throw new InvalidOperationException("GlobalSetup ist nicht gelaufen.");

    /// <summary>Öffnet den indizierten Bestand und prüft, dass er Dateien enthält.</summary>
    /// <returns>Ein Task, der nach dem Aufbau endet.</returns>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _stock = await IndexedStock.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        AsyncServiceScope scope = Stock.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            int indexed = await scope.ServiceProvider
                .GetRequiredService<IMarkdownFileRepository>()
                .CountAsync(CancellationToken.None)
                .ConfigureAwait(false);
            MeasurementCheck.RequireRows(indexed, "Dateien im Index");
        }
    }

    /// <summary>Gibt den Bestand frei.</summary>
    /// <returns>Ein Task, der nach dem Freigeben endet.</returns>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        if (_stock is not null)
        {
            await _stock.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Nur das Aufzählen samt Ausschlussregeln, ohne Datenbank: der Anteil des Abgleichs, der am
    /// Dateisystem hängt. Die Differenz zur vollen Messung ist der eigene Vergleich.
    /// </summary>
    /// <returns>Anzahl der gefundenen Dateien.</returns>
    [Benchmark]
    public int EnumerateOnly() =>
        Stock.Services.GetRequiredService<IFileScanner>().EnumerateMarkdownFiles(Stock.RootPath, CancellationToken.None).Count();

    /// <summary>
    /// Ein Takt der Volltext-Pflege über einen unveränderten Bestand (Vorgabe alle 5 Sekunden): alle
    /// Index-Kennungen und alle Dokumente mit Schlagworten laden und vergleichen.
    /// </summary>
    /// <returns>Anzahl geschriebener Einträge, hier 0.</returns>
    [Benchmark]
    public Task<int> SearchIndexMaintenanceWithoutChanges() =>
        Stock.Services.GetRequiredService<Fts5IndexMaintainer>().SynchronizeAsync(CancellationToken.None);

    /// <summary>Ein Abgleich, der nichts zu schreiben findet.</summary>
    /// <returns>Ein Task, der nach dem Abgleich endet.</returns>
    [Benchmark]
    public Task ResyncWithoutChanges() =>
        Stock.Services.GetRequiredService<IIndexer>().RunInitialScanAsync(CancellationToken.None);
}
