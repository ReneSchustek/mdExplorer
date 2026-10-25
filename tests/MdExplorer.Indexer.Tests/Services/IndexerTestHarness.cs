using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Indexer.Abstractions;
using MdExplorer.Indexer.Models;
using MdExplorer.Indexer.Options;
using MdExplorer.Indexer.Services;
using MdExplorer.Indexer.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.Indexer.Tests.Services;

/// <summary>
/// Baut den echten <see cref="MarkdownIndexer"/> samt Scanner, Hash und Watcher-Koordinator über
/// Attrappen für Dateisystem, Bestand, Watcher und Uhr zusammen.
/// </summary>
/// <remarks>
/// Gewartet wird nie auf eine Pause, sondern auf ein Ereignis: den Abschluss des Erst-Scans, ein
/// Speichern im Bestand, einen angelegten Zeitgeber. Feste Pausen sind unter paralleler Last mal
/// zu kurz und machen negative Prüfungen wertlos, weil ein Fehler einfach noch nicht passiert war.
/// </remarks>
internal sealed class IndexerTestHarness : IAsyncDisposable
{
    public const string DefaultRoot = @"C:\Wurzel";

    public const int DebounceMs = 300;

    // Obergrenze für das Warten auf ein Signal. Sie greift nur, wenn etwas hängt; im Normalfall
    // kommt das Signal in Millisekunden.
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly ServiceProvider _provider;
    private readonly FileWatcherCoordinator _coordinator;

    private IndexerTestHarness(
        ServiceProvider provider,
        FakeFileSystem fileSystem,
        FakeMarkdownFileStore store,
        FakeFileWatcherFactory watcherFactory,
        SignalingTimeProvider timeProvider,
        FakeSettingsService settings,
        FileWatcherCoordinator coordinator,
        MarkdownIndexer indexer,
        IReadOnlyList<string> roots)
    {
        _provider = provider;
        _coordinator = coordinator;
        FileSystem = fileSystem;
        Store = store;
        WatcherFactory = watcherFactory;
        TimeProvider = timeProvider;
        Settings = settings;
        Indexer = indexer;
        Roots = roots;
    }

    public FakeFileSystem FileSystem { get; }

    public FakeMarkdownFileStore Store { get; }

    public FakeFileWatcherFactory WatcherFactory { get; }

    public SignalingTimeProvider TimeProvider { get; }

    public FakeSettingsService Settings { get; }

    public MarkdownIndexer Indexer { get; }

    public IReadOnlyList<string> Roots { get; }

    public FakeFileWatcher WatcherFor(string root) => WatcherFactory.Watchers[root];

    public static IndexerTestHarness Create(
        TimeSpan? resyncInterval = null,
        IReadOnlyList<string>? roots = null,
        int initialScanBatchSize = 100,
        IExclusionFilter? exclusionFilter = null,
        FakeFileWatcherFactory? watcherFactory = null,
        FakeFileSystem? fileSystem = null)
    {
        fileSystem ??= new FakeFileSystem();
        FakeMarkdownFileStore store = new();
        FakeFileWatcherFactory factory = watcherFactory ?? new FakeFileWatcherFactory();
        SignalingTimeProvider timeProvider = new();
        IndexerOptions options = new()
        {
            DebounceMs = DebounceMs,
            BatchSize = 50,
            InitialScanBatchSize = initialScanBatchSize,
        };
        IReadOnlyList<string> effectiveRoots = roots ?? [DefaultRoot];
        foreach (string root in effectiveRoots)
        {
            fileSystem.AddDirectory(root);
        }

        FakeSettingsService settings = new(BuildSettings(effectiveRoots, resyncInterval ?? TimeSpan.Zero));

        ServiceCollection services = new();
        _ = services.AddScoped<IMarkdownFileRepository>(_ => store.CreateSession());
        ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        IExclusionFilter filter = exclusionFilter ?? new PassThroughExclusionFilter();
        FileScanner scanner = new(fileSystem, filter, options.ToOptions(), NullLogger<FileScanner>.Instance);
        FileWatcherCoordinator coordinator = new(factory, options.ToOptions(), timeProvider, NullLogger<FileWatcherCoordinator>.Instance);
        MarkdownIndexer indexer = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            scanner,
            new HashCalculator(fileSystem),
            fileSystem,
            coordinator,
            settings,
            options.ToOptions(),
            timeProvider,
            new MdExplorer.Core.Diagnostics.BackgroundServiceStatus(),
            NullLogger<MarkdownIndexer>.Instance);

        return new IndexerTestHarness(provider, fileSystem, store, factory, timeProvider, settings, coordinator, indexer, effectiveRoots);
    }

    /// <summary>Startet den Dienst und wartet, bis der Erst-Scan jeder Wurzel abgeschlossen ist.</summary>
    public async Task StartAndWaitForInitialScanAsync()
    {
        Task scanned = WaitForCompletionsAsync(Roots.Count);
        await Indexer.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        await scanned.WaitAsync(Patience, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Liefert eine Aufgabe, die endet, sobald <paramref name="count"/> Wurzeln einen Durchgang abgeschlossen haben.</summary>
    public Task WaitForCompletionsAsync(int count)
    {
        TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int seen = 0;
        if (count == 0)
        {
            done.SetResult();
            return done.Task;
        }
        Indexer.InitialScanProgress += (_, args) =>
        {
            if (args.IsCompleted && Interlocked.Increment(ref seen) == count)
            {
                _ = done.TrySetResult();
            }
        };
        return done.Task;
    }

    /// <summary>
    /// Löst ein Watcher-Ereignis aus, lässt die Entprellung ablaufen und wartet, bis der
    /// Ereignisverbraucher den Stapel gespeichert hat. Der Verbraucher speichert jeden Stapel,
    /// auch einen ohne Änderung; danach ist ein ausbleibender Schreibzugriff wirklich ausgeblieben.
    /// </summary>
    public async Task ProcessAsync(FileSystemEvent fileSystemEvent)
    {
        long mark = Store.Writes.Mark;
        WatcherFor(fileSystemEvent.Root).TriggerEvent(fileSystemEvent);
        TimeProvider.Advance(TimeSpan.FromMilliseconds(DebounceMs));
        Assert.True(
            await Store.Writes.WaitAsync(mark, 1, Patience, TestContext.Current.CancellationToken).ConfigureAwait(false),
            "Der Ereignisverbraucher hat den Stapel nicht gespeichert.");
    }

    /// <summary>Stellt die Uhr um ein Intervall vor und wartet, bis der ausgelöste Abgleich die Wurzel gelesen hat.</summary>
    public async Task RunOneResyncAsync(TimeSpan interval)
    {
        long mark = Store.RootQueries.Mark;
        TimeProvider.Advance(interval);
        Assert.True(
            await Store.RootQueries.WaitAsync(mark, 1, Patience, TestContext.Current.CancellationToken).ConfigureAwait(false),
            "Kein Durchgang über die Wurzel.");
    }

    /// <summary>
    /// Wartet Schreibzugriff um Schreibzugriff, bis <paramref name="condition"/> gilt. Geprüft wird nach
    /// jedem Schreiben, nie nach einer Pause.
    /// </summary>
    public async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        ArgumentNullException.ThrowIfNull(condition);
        while (true)
        {
            long mark = Store.Writes.Mark;
            if (condition())
            {
                return;
            }
            Assert.True(await Store.Writes.WaitAsync(mark, 1, Patience, TestContext.Current.CancellationToken).ConfigureAwait(false), because);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Indexer.StopAsync(CancellationToken.None).ConfigureAwait(false);
        await _coordinator.DisposeAsync().ConfigureAwait(false);
        await _provider.DisposeAsync().ConfigureAwait(false);
    }

    private static AppSettings BuildSettings(IReadOnlyList<string> roots, TimeSpan resyncInterval)
    {
        // 3.600 Sekunden ist die Obergrenze, die SettingsValidator für den Abgleich zulässt.
        int resyncSeconds = (int)Math.Clamp(resyncInterval.TotalSeconds, 0, 3_600);
        BehaviorSettings behavior = new(SearchDebounceMs: 300, IndexerResyncIntervalSeconds: resyncSeconds);
        return new AppSettings(
            AppSettings.CurrentSchemaVersion,
            new IndexingSettings([.. roots], [], [], true),
            AppearanceSettings.Default,
            behavior);
    }
}
