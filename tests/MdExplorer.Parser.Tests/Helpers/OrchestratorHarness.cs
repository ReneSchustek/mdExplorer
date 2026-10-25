using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Diagnostics;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.Options;
using MdExplorer.Parser.Services;
using MdExplorer.Parser.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MEOptions = Microsoft.Extensions.Options.Options;

namespace MdExplorer.Parser.Tests.Helpers;

/// <summary>
/// Echter <see cref="ParseOrchestrator"/> über Attrappen für Quellen, Dateisystem und Repositories,
/// mit fester Uhr und festen Kennungen.
/// </summary>
/// <remarks>
/// Die Schleife wird ohne Wanduhr getaktet: Ein Durchlauf meldet sein Ende im Protokoll
/// (<see cref="RunEndEventIds"/>), und erst danach stellt <see cref="RunNextTickAsync"/> die Uhr
/// um ein Intervall weiter. Der Zeitgeber des Orchestrators entsteht vor dem ersten Lauf und
/// merkt sich einen Takt, der eintrifft, während der Lauf noch arbeitet.
/// </remarks>
internal sealed class OrchestratorHarness : IAsyncDisposable, IDisposable
{
    /// <summary>Die Voreinstellung von <see cref="ParserOptions.PollIntervalSeconds"/>.</summary>
    public const int IntervalSeconds = 5;

    // Durchlauf abgeschlossen, Durchlauf übersprungen, Durchlauf weiterhin übersprungen.
    private static readonly int[] RunEndEventIds = [202, 207, 213];

    // Schutz gegen einen hängenden Test; im Normalfall endet jedes Warten nach Millisekunden.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly ServiceProvider _provider;
    private readonly FakeTimeProvider _time = new();
    private int _nextId;
    private bool _started;

    public OrchestratorHarness(
        IMarkdownParser? parser = null,
        int batchSize = 100,
        long maxFileSizeBytes = 4L * 1024 * 1024,
        Func<IServiceScopeFactory, IServiceScopeFactory>? decorateScopes = null)
    {
        DocRepo.OnSaveChangesAsync = ct => TagRepo.SaveChangesAsync(ct);

        ServiceCollection services = new();
        _ = services.AddSingleton<IMarkdownSourceProvider>(Source);
        _ = services.AddSingleton<IMarkdownDocumentRepository>(DocRepo);
        _ = services.AddSingleton<ITagRepository>(TagRepo);
        _ = services.AddSingleton<IParseFailureRepository>(FailureRepo);
        _provider = services.BuildServiceProvider();
        IServiceScopeFactory scopes = _provider.GetRequiredService<IServiceScopeFactory>();
        ScopeFactory = decorateScopes is null ? scopes : decorateScopes(scopes);

        // Eine feste Parallelität statt der Voreinstellung (Zahl der Prozessoren) macht den Lauf
        // unabhängig vom Rechner.
        ParserOptions options = new()
        {
            MaxParallelism = 2,
            BatchSize = batchSize,
            PollIntervalSeconds = IntervalSeconds,
            MaxFileSizeBytes = maxFileSizeBytes,
        };
        Sut = new ParseOrchestrator(
            ScopeFactory, FileSystem, parser ?? CreateParser(), FailureStatus, MEOptions.Create(options), _time, ServiceStatus, Logger);
    }

    public FakeFileSystem FileSystem { get; } = new();

    public MdExplorer.Core.Diagnostics.BackgroundServiceStatus ServiceStatus { get; } = new();

    public FakeMarkdownSourceProvider Source { get; } = new();

    public FakeMarkdownDocumentRepository DocRepo { get; } = new();

    public FakeTagRepository TagRepo { get; } = new();

    public FakeParseFailureRepository FailureRepo { get; } = new();

    public ParseFailureStatus FailureStatus { get; } = new();

    public RecordingLogger<ParseOrchestrator> Logger { get; } = new();

    public IServiceScopeFactory ScopeFactory { get; }

    public ParseOrchestrator Sut { get; }

    public static MarkdigParser CreateParser() => new(
        new FrontmatterExtractor(),
        new TagExtractor(new FakeSettingsService()),
        new WikiLinkExtractor(),
        new TagNormalizer());

    public Guid AddSource(string path, string contentHash, string content)
    {
        Guid id = new(Interlocked.Increment(ref _nextId), 0, 0, new byte[8]);
        string normalized = path.Replace('/', Path.DirectorySeparatorChar);
        FileSystem.AddFile(normalized, content);
        Source.Add(new MarkdownSourceSnapshot(id, normalized, contentHash));
        return id;
    }

    public void UpdateSource(Guid fileId, string newHash, string newContent)
    {
        Source.Replace(fileId, existing =>
        {
            FileSystem.AddFile(existing.AbsolutePath, newContent);
            return existing with { ContentHash = newHash };
        });
    }

    /// <summary>Startet die Schleife und wartet, bis der erste Durchlauf zu Ende ist.</summary>
    public async Task StartAsync()
    {
        _started = true;
        await Sut.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        await WaitForRunsAsync(1).ConfigureAwait(false);
    }

    /// <summary>Stellt die Uhr um ein Intervall weiter und wartet, bis der nächste Durchlauf zu Ende ist.</summary>
    public async Task RunNextTickAsync()
    {
        int finished = FinishedRuns();
        _time.Advance(TimeSpan.FromSeconds(IntervalSeconds));
        await WaitForRunsAsync(finished + 1).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_started)
        {
            await Sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
        Sut.Dispose();
        await _provider.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Für Tests, die einzelne Durchläufe direkt aufrufen und die Schleife nie starten.</summary>
    public void Dispose()
    {
        if (_started)
        {
            throw new InvalidOperationException("Eine gestartete Schleife wird über DisposeAsync beendet.");
        }
        Sut.Dispose();
        _provider.Dispose();
    }

    private static bool IsRunEnd(RecordedLogEntry entry) => RunEndEventIds.Contains(entry.EventId);

    private int FinishedRuns() => Logger.Entries.Count(IsRunEnd);

    private Task WaitForRunsAsync(int count) => Logger.WaitForCountAsync(IsRunEnd, count, Patience);
}
