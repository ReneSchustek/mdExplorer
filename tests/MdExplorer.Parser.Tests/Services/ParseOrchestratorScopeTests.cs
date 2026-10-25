using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Diagnostics;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.Options;
using MdExplorer.Parser.Services;
using MdExplorer.Parser.Tests.Fakes;
using MdExplorer.Parser.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MEOptions = Microsoft.Extensions.Options.Options;

namespace MdExplorer.Parser.Tests.Services;

/// <summary>
/// Hält fest, dass jeder Stapel in einem eigenen Bereich schreibt.
/// </summary>
/// <remarks>
/// Liefe ein ganzer Durchlauf in einem einzigen Bereich, sammelte der Änderungsverfolger
/// jedes Dokument und jedes Schlagwort des gesamten Bestands, und jedes Schreiben durchliefe
/// alles bereits Verfolgte. Über 29.889 Dateien sind das 9 GB Arbeitsspeicher und ein
/// Stapel, der von einer Sekunde auf zweieinhalb Minuten anwächst; der Durchlauf käme nie
/// ans Ende, und damit liefe auch der Aufräumdurchgang des Indexers nie.
/// </remarks>
public sealed class ParseOrchestratorScopeTests
{
    [Fact]
    public async Task RunOnce_OnManyFiles_OpensOneScopePerBatch()
    {
        // Fünf Dateien bei Stapelgröße zwei: drei Stapel, dazu der Lesebereich, der Bereich
        // fürs Wegräumen der Schlagworte ohne Datei und der, in dem die Zahl der nicht
        // verarbeitbaren Dateien gelesen wird.
        using OrchestratorHarness harness = new(batchSize: 2, decorateScopes: inner => new CountingScopeFactory(inner));
        for (int i = 0; i < 5; i++)
        {
            _ = harness.AddSource($"/r/datei-{i}.md", $"hash-{i}", $"# Titel {i}");
        }

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(6, ((CountingScopeFactory)harness.ScopeFactory).Created);
    }

    [Fact]
    public async Task RunOnce_OnEmptySource_StillRunsTheCleanup()
    {
        // Ein Schlagwort verliert seine letzte Datei meist durch den Indexer, nicht durch den
        // Parser. Ein Durchlauf ohne eigene Arbeit muss deshalb trotzdem aufräumen.
        using OrchestratorHarness harness = new(batchSize: 2, decorateScopes: inner => new CountingScopeFactory(inner));

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, ((CountingScopeFactory)harness.ScopeFactory).Created);
    }

    [Theory]
    [InlineData(6, 3, 5)]
    [InlineData(6, 6, 4)]
    [InlineData(7, 3, 6)]
    public async Task RunOnce_OnAnyBatchSize_OpensReadScopePlusOnePerBatch(
        int fileCount,
        int batchSize,
        int expectedScopes)
    {
        // Erwartet: ein Lesebereich, einer je Stapel, einer fürs Aufräumen und einer für den
        // Betriebs-Stand am Ende.
        // Die Zahl hängt damit an der Stapelgröße. Ohne den eigenen Bereich je Stapel stünde
        // hier dieselbe Zahl, egal wie groß der Bestand wird.
        using OrchestratorHarness harness = new(batchSize: batchSize, decorateScopes: inner => new CountingScopeFactory(inner));
        for (int i = 0; i < fileCount; i++)
        {
            _ = harness.AddSource($"/r/datei-{i}.md", $"hash-{i}", $"# Titel {i}");
        }

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expectedScopes, ((CountingScopeFactory)harness.ScopeFactory).Created);
    }

    /// <summary>Zählt, wie oft ein Bereich aufgemacht wurde; mehr braucht die Zusicherung nicht.</summary>
    private sealed class CountingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        public int Created { get; private set; }

        public IServiceScope CreateScope()
        {
            Created++;
            return inner.CreateScope();
        }
    }
}
