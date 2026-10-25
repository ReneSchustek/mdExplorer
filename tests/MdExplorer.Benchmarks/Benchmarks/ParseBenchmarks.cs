using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using BenchmarkDotNet.Attributes;
using MdExplorer.Benchmarks.Stock;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.Benchmarks.Benchmarks;

/// <summary>
/// Ein Dokument durch die Index-Pipeline: Markdown lesen, Frontmatter, Schlagworte und Verweise
/// herausziehen, HTML für die Vorschau erzeugen und packen.
/// </summary>
/// <remarks>
/// Das passiert beim ersten Start für jede Datei des Bestands und danach bei jeder Änderung. Eine
/// typische Notiz zeigt die Kosten pro Datei; die längste Notiz zeigt, ob eine einzelne große Datei
/// den Durchlauf aufhält.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet erzeugt zu jeder Messklasse eine abgeleitete Klasse in einer eigenen Baugruppe und verlangt dafür einen öffentlichen, nicht versiegelten Typ.")]
public class ParseBenchmarks
{
    // Die Notiz in der Mitte der nach Länge sortierten Liste: der Median.
    private const int MedianDivisor = 2;

    private IndexedStock? _stock;
    private IMarkdownParser? _parser;

    // BenchmarkDotNet ruft GlobalSetup vor jeder Messung; fehlt der Parser, ist das ein Fehler im
    // Aufbau und keine Messung wert.
    private IMarkdownParser Parser => _parser ?? throw new InvalidOperationException("GlobalSetup ist nicht gelaufen.");
    private string _typicalNote = string.Empty;
    private string _largestNote = string.Empty;

    /// <summary>Wählt die Notiz mittlerer Länge und die längste Notiz des Bestands.</summary>
    /// <returns>Ein Task, der nach dem Aufbau endet.</returns>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _stock = await IndexedStock.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        _parser = _stock.Services.GetRequiredService<IMarkdownParser>();

        List<FileInfo> notes = [.. new DirectoryInfo(_stock.RootPath)
            .EnumerateFiles("*.md", SearchOption.AllDirectories)
            .OrderBy(file => file.Length)];
        MeasurementCheck.RequireRows(notes.Count, "Notizen im Messbestand");

        _typicalNote = await File.ReadAllTextAsync(notes[notes.Count / MedianDivisor].FullName).ConfigureAwait(false);
        _largestNote = await File.ReadAllTextAsync(notes[^1].FullName).ConfigureAwait(false);
        // Geprüft wird das entpackte HTML: gzip liefert auch für eine leere Ausgabe ein paar Byte,
        // die gepackte Länge könnte also nie anschlagen.
        MeasurementCheck.RequireRows(RenderedLength(_typicalNote), nameof(TypicalNote));
        MeasurementCheck.RequireRows(RenderedLength(_largestNote), nameof(LargestNote));
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

    /// <summary>Notiz mittlerer Länge.</summary>
    /// <returns>Länge des gepackten HTML in Byte.</returns>
    [Benchmark(Baseline = true)]
    public int TypicalNote() => Parse(_typicalNote);

    /// <summary>Längste Notiz des Bestands.</summary>
    /// <returns>Länge des gepackten HTML in Byte.</returns>
    [Benchmark]
    public int LargestNote() => Parse(_largestNote);

    private int Parse(string markdown)
    {
        ParseResult result = Parser.Parse(markdown);
        return result.RenderedHtmlGz.Length;
    }

    private int RenderedLength(string markdown)
    {
        using MemoryStream packed = new(Parser.Parse(markdown).RenderedHtmlGz.ToArray());
        using GZipStream unpacked = new(packed, CompressionMode.Decompress);
        using StreamReader reader = new(unpacked);
        return reader.ReadToEnd().Length;
    }
}
