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
/// Bis zum 16.08.2026 lief ein ganzer Durchlauf in einem einzigen Bereich. Der
/// Änderungsverfolger sammelte damit jedes Dokument und jedes Schlagwort des gesamten
/// Bestands, und jedes Schreiben durchlief alles bereits Verfolgte. Über 29.889 Dateien
/// waren das 9 GB Arbeitsspeicher und ein Stapel, der von einer Sekunde auf zweieinhalb
/// Minuten anwuchs — der Durchlauf kam nie ans Ende, und damit lief auch der
/// Aufräumdurchgang des Indexers nie.
/// </remarks>
public sealed class ParseOrchestratorScopeTests
{
    [Fact]
    public async Task RunOnce_OnManyFiles_OpensOneScopePerBatch()
    {
        // Fünf Dateien bei Stapelgröße zwei: drei Stapel, dazu der Lesebereich, der Bereich
        // fürs Wegräumen der Schlagworte ohne Datei und der, in dem die Zahl der nicht
        // verarbeitbaren Dateien gelesen wird.
        ScopeCountingHarness harness = new(batchSize: 2);
        for (int i = 0; i < 5; i++)
        {
            harness.AddSource($"/r/datei-{i}.md", $"hash-{i}", $"# Titel {i}");
        }

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(6, harness.ScopeFactory.Created);
    }

    [Fact]
    public async Task RunOnce_OnEmptySource_StillRunsTheCleanup()
    {
        // Ein Schlagwort verliert seine letzte Datei meist durch den Indexer, nicht durch den
        // Parser. Ein Durchlauf ohne eigene Arbeit muss deshalb trotzdem aufräumen.
        ScopeCountingHarness harness = new(batchSize: 2);

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, harness.ScopeFactory.Created);
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
        // Die Zahl hängt damit an der Stapelgröße, nicht am Bestand. Ohne den eigenen
        // Bereich je Stapel stünde hier immer 2 — egal wie groß der Bestand wird.
        ScopeCountingHarness harness = new(batchSize);
        for (int i = 0; i < fileCount; i++)
        {
            harness.AddSource($"/r/datei-{i}.md", $"hash-{i}", $"# Titel {i}");
        }

        await harness.Sut.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expectedScopes, harness.ScopeFactory.Created);
    }

    private sealed class ScopeCountingHarness
    {
        public FakeFileSystem FileSystem { get; } = new();
        public FakeMarkdownSourceProvider Source { get; } = new();
        public FakeMarkdownDocumentRepository DocRepo { get; } = new();
        public FakeTagRepository TagRepo { get; } = new();
        public FakeParseFailureRepository FailureRepo { get; } = new();
        public ParseFailureStatus FailureStatus { get; } = new();
        public CountingScopeFactory ScopeFactory { get; }
        public ParseOrchestrator Sut { get; }

        public ScopeCountingHarness(int batchSize)
        {
            DocRepo.OnSaveChangesAsync = ct => TagRepo.SaveChangesAsync(ct);

            ServiceCollection services = new();
            _ = services.AddSingleton<IMarkdownSourceProvider>(Source);
            _ = services.AddSingleton<IMarkdownDocumentRepository>(DocRepo);
            _ = services.AddSingleton<ITagRepository>(TagRepo);
            _ = services.AddSingleton<IParseFailureRepository>(FailureRepo);
            ServiceProvider provider = services.BuildServiceProvider();

            ScopeFactory = new CountingScopeFactory(provider.GetRequiredService<IServiceScopeFactory>());

            TagNormalizer normalizer = new();
            MarkdigParser parser = new(
                new FrontmatterExtractor(),
                new TagExtractor(new FakeSettingsService()),
                new WikiLinkExtractor(),
                normalizer);
            ParserOptions parserOptions = new()
            {
                MaxParallelism = 2,
                BatchSize = batchSize,
                PollIntervalSeconds = 1,
            };

            Sut = new ParseOrchestrator(
                ScopeFactory,
                FileSystem,
                parser,
                FailureStatus,
                MEOptions.Create(parserOptions),
                new FakeTimeProvider(),
                NullLogger<ParseOrchestrator>.Instance);
        }

        public void AddSource(string path, string contentHash, string content)
        {
            string normalized = path.Replace('/', Path.DirectorySeparatorChar);
            FileSystem.AddFile(normalized, content);
            Source.Sources.Add(new MarkdownSourceSnapshot(Guid.NewGuid(), normalized, contentHash));
        }
    }

    /// <summary>Zählt, wie oft ein Bereich aufgemacht wurde — mehr braucht die Zusicherung nicht.</summary>
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
