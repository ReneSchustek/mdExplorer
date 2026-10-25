using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using MdExplorer.Benchmarks.Stock;
using MdExplorer.Core.Abstractions;
using MdExplorer.Graph.Abstractions;
using MdExplorer.Graph.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.Benchmarks.Benchmarks;

/// <summary>
/// Auswertungen über den vollen Bestand: der Graph über alle Dateien und die Schlagwort-Statistik
/// der Tag-Cloud.
/// </summary>
/// <remarks>
/// Beide laufen beim Öffnen eines Fensters und nach jedem Indexer-Lauf. Gemessen werden die
/// Abfragen mit Aufbereitung, ohne Zeichnen; das Zeichnen liegt im Browser-Steuerelement und hängt
/// an der Grafikkarte, nicht am eigenen Code.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet erzeugt zu jeder Messklasse eine abgeleitete Klasse in einer eigenen Baugruppe und verlangt dafür einen öffentlichen, nicht versiegelten Typ.")]
public class GraphAndTagBenchmarks
{
    // Die Vorgaben der Tag-Cloud: zugeklappt die häufigsten 50, aufgeklappt die lange Liste und
    // die Tag-Verwaltung mit bis zu 1.000.
    private const int CloudTopN = 50;
    private const int LongTailTopN = 1_000;

    private IndexedStock? _stock;
    private string _clickTarget = string.Empty;

    // BenchmarkDotNet ruft GlobalSetup vor jeder Messung; fehlt der Bestand, ist das ein Fehler
    // im Aufbau und keine Messung wert.
    private IndexedStock Stock => _stock ?? throw new InvalidOperationException("GlobalSetup ist nicht gelaufen.");

    /// <summary>Öffnet den Bestand und prüft, dass jede Auswertung Zeilen liefert.</summary>
    /// <returns>Ein Task, der nach dem Aufbau endet.</returns>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _stock = await IndexedStock.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        MeasurementCheck.RequireRows(await GraphOverAllFiles().ConfigureAwait(false), nameof(GraphOverAllFiles));
        MeasurementCheck.RequireRows(await TopTagsForTheCloud().ConfigureAwait(false), nameof(TopTagsForTheCloud));
        MeasurementCheck.RequireRows(await TopTagsLongTail().ConfigureAwait(false), nameof(TopTagsLongTail));

        AsyncServiceScope scope = Stock.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            AllFilesRow first = (await scope.ServiceProvider
                .GetRequiredService<IAllFilesQuery>()
                .GetAllAsync(CancellationToken.None)
                .ConfigureAwait(false))[0];
            _clickTarget = first.Title;
        }
        MeasurementCheck.RequireRows(await WikiLinkClick().ConfigureAwait(false), nameof(WikiLinkClick));
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

    /// <summary>Graph über alle Dateien ohne Filter.</summary>
    /// <returns>Anzahl der Kanten.</returns>
    [Benchmark]
    public async Task<int> GraphOverAllFiles()
    {
        AsyncServiceScope scope = Stock.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            GraphSnapshot snapshot = await scope.ServiceProvider
                .GetRequiredService<IGraphService>()
                .BuildSnapshotAsync(GraphFilter.None, CancellationToken.None)
                .ConfigureAwait(false);
            return snapshot.Edges.Count;
        }
    }

    /// <summary>Häufigste Schlagworte für die zugeklappte Cloud.</summary>
    /// <returns>Anzahl der Zeilen.</returns>
    [Benchmark]
    public Task<int> TopTagsForTheCloud() => TopTagsAsync(CloudTopN);

    /// <summary>Lange Liste für die aufgeklappte Cloud und die Tag-Verwaltung.</summary>
    /// <returns>Anzahl der Zeilen.</returns>
    [Benchmark]
    public Task<int> TopTagsLongTail() => TopTagsAsync(LongTailTopN);

    /// <summary>
    /// Ein Klick auf einen WikiLink: Die Auflösung lädt dafür die Stammdaten aller Dateien und
    /// baut den Slug-Index neu. Ob sich ein Zwischenspeicher lohnt, entscheidet diese Zahl.
    /// </summary>
    /// <returns>1, wenn das Ziel gefunden wurde.</returns>
    [Benchmark]
    public async Task<int> WikiLinkClick()
    {
        AsyncServiceScope scope = Stock.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            Guid? target = await scope.ServiceProvider
                .GetRequiredService<IWikiLinkResolver>()
                .ResolveAsync(_clickTarget, CancellationToken.None)
                .ConfigureAwait(false);
            return target is null ? 0 : 1;
        }
    }

    private async Task<int> TopTagsAsync(int topN)
    {
        AsyncServiceScope scope = Stock.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            IReadOnlyList<TagStatisticRow> rows = await scope.ServiceProvider
                .GetRequiredService<ITagStatisticsQuery>()
                .GetTopTagsAsync(topN, CancellationToken.None)
                .ConfigureAwait(false);
            return rows.Count;
        }
    }
}
