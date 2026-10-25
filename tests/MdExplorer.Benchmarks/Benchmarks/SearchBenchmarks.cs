using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using MdExplorer.Benchmarks.Stock;
using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.Benchmarks.Benchmarks;

/// <summary>
/// Suche über den vollen Bestand. Sie läuft nach jedem Tastendruck; was hier langsam ist, spürt der
/// Nutzer beim Tippen.
/// </summary>
/// <remarks>
/// Jede Abfrage öffnet einen eigenen Bereich mit eigenem Datenbankkontext, wie die Anwendung je
/// Suche. Die Kosten dafür gehören zur Antwortzeit, die der Nutzer sieht.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet erzeugt zu jeder Messklasse eine abgeleitete Klasse in einer eigenen Baugruppe und verlangt dafür einen öffentlichen, nicht versiegelten Typ.")]
public class SearchBenchmarks
{
    private static readonly SearchQuery SimpleTermQuery = new(StockGenerator.MediumFrequencyTerm);
    private static readonly SearchQuery PhraseQuery = new("\"" + StockGenerator.Phrase + "\"");
    private static readonly SearchQuery TagQuery = new("tag:" + StockGenerator.FrequentTag);
    private static readonly SearchQuery StemmedQuery = new(StockGenerator.StemmedTerm, Similarity: SimilarityMode.Stemmed);

    // Dritte Seite: Der Vorfilter muss die vorderen Seiten mitholen, bevor der Ausdruck prüft.
    private static readonly SearchQuery RegexLaterPageQuery = new("Rechnung(en)?", Take: 20, Skip: 40, Mode: SearchMode.Regex);

    private IndexedStock? _stock;

    // BenchmarkDotNet ruft GlobalSetup vor jeder Messung; fehlt der Bestand, ist das ein Fehler
    // im Aufbau und keine Messung wert.
    private IndexedStock Stock => _stock ?? throw new InvalidOperationException("GlobalSetup ist nicht gelaufen.");

    /// <summary>Öffnet den Bestand und prüft, dass jede Abfrage Treffer liefert.</summary>
    /// <returns>Ein Task, der nach dem Aufbau endet.</returns>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _stock = await IndexedStock.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        MeasurementCheck.RequireRows(await SimpleTerm().ConfigureAwait(false), nameof(SimpleTerm));
        MeasurementCheck.RequireRows(await Phrase().ConfigureAwait(false), nameof(Phrase));
        MeasurementCheck.RequireRows(await Tag().ConfigureAwait(false), nameof(Tag));
        MeasurementCheck.RequireRows(await Stemmed().ConfigureAwait(false), nameof(Stemmed));
        MeasurementCheck.RequireRows(await RegexLaterPage().ConfigureAwait(false), nameof(RegexLaterPage));
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

    /// <summary>Ein mittelhäufiger Begriff, der Normalfall beim Tippen.</summary>
    /// <returns>Anzahl der Treffer.</returns>
    [Benchmark(Baseline = true)]
    public Task<int> SimpleTerm() => SearchAsync(SimpleTermQuery);

    /// <summary>Eine Wortgruppe in Anführungszeichen.</summary>
    /// <returns>Anzahl der Treffer.</returns>
    [Benchmark]
    public Task<int> Phrase() => SearchAsync(PhraseQuery);

    /// <summary>Einschränkung auf das häufigste Schlagwort.</summary>
    /// <returns>Anzahl der Treffer.</returns>
    [Benchmark]
    public Task<int> Tag() => SearchAsync(TagQuery);

    /// <summary>Suche über den Wortstamm.</summary>
    /// <returns>Anzahl der Treffer.</returns>
    [Benchmark]
    public Task<int> Stemmed() => SearchAsync(StemmedQuery);

    /// <summary>Regulärer Ausdruck, dritte Ergebnisseite.</summary>
    /// <returns>Anzahl der Treffer.</returns>
    [Benchmark]
    public Task<int> RegexLaterPage() => SearchAsync(RegexLaterPageQuery);

    private async Task<int> SearchAsync(SearchQuery query)
    {
        AsyncServiceScope scope = Stock.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            IReadOnlyList<SearchResult> results = await scope.ServiceProvider
                .GetRequiredService<ISearchService>()
                .SearchAsync(query, CancellationToken.None)
                .ConfigureAwait(false);
            return results.Count;
        }
    }
}
