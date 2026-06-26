using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Indexer.Abstractions;
using MdExplorer.Indexer.Options;
using MdExplorer.Indexer.Services;
using MdExplorer.Indexer.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.Indexer.Tests.Services;

/// <summary>
/// Bündelt alle Fakes und konkreten Indexer-Komponenten zu einer testbaren Einheit.
/// </summary>
internal sealed class IndexerTestHarness : IAsyncDisposable
{
    public const string DefaultRoot = @"C:\Wurzel";

    private readonly ServiceProvider _provider;
    private readonly FileWatcherCoordinator _coordinator;

    private IndexerTestHarness(
        ServiceProvider provider,
        FakeFileSystem fileSystem,
        FakeMarkdownFileRepository repository,
        FakeFileWatcherFactory watcherFactory,
        FakeTimeProvider timeProvider,
        FakeSettingsService settings,
        FileWatcherCoordinator coordinator,
        MarkdownIndexer indexer)
    {
        _provider = provider;
        _coordinator = coordinator;
        FileSystem = fileSystem;
        Repository = repository;
        WatcherFactory = watcherFactory;
        TimeProvider = timeProvider;
        Settings = settings;
        Indexer = indexer;
    }

    public FakeFileSystem FileSystem { get; }

    public FakeMarkdownFileRepository Repository { get; }

    public FakeFileWatcherFactory WatcherFactory { get; }

    public FakeTimeProvider TimeProvider { get; }

    public FakeSettingsService Settings { get; }

    public MarkdownIndexer Indexer { get; }

    public FakeFileWatcher WatcherFor(string root) => WatcherFactory.Watchers[root];

    public static IndexerTestHarness Create(
        int debounceMs = 300,
        TimeSpan? resyncInterval = null,
        IReadOnlyList<string>? roots = null,
        int initialScanBatchSize = 100)
    {
        FakeFileSystem fileSystem = new();
        FakeMarkdownFileRepository repository = new();
        FakeFileWatcherFactory watcherFactory = new();
        FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 6, 7, 12, 0, 0, TimeSpan.Zero));

        IndexerOptions options = new()
        {
            DebounceMs = debounceMs,
            BatchSize = 50,
            BatchFlushIntervalMs = 500,
            InitialScanBatchSize = initialScanBatchSize,
        };
        IReadOnlyList<string> effectiveRoots = roots ?? [DefaultRoot];
        foreach (string root in effectiveRoots)
        {
            fileSystem.AddDirectory(root);
        }

        int resyncSeconds = (int)Math.Clamp(
            (resyncInterval ?? TimeSpan.Zero).TotalSeconds,
            0,
            3_600);
        BehaviorSettings behavior = new(SearchDebounceMs: 300, IndexerResyncIntervalSeconds: resyncSeconds);
        AppSettings appSettings = new(
            AppSettings.CurrentSchemaVersion,
            new IndexingSettings([.. effectiveRoots], [], [], true),
            AppearanceSettings.Default,
            behavior);
        FakeSettingsService settings = new(appSettings);

        ServiceCollection services = new();
        _ = services.AddSingleton<IMarkdownFileRepository>(repository);

        ServiceProvider provider = services.BuildServiceProvider();
        IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        IExclusionFilter exclusionFilter = new PassThroughExclusionFilter();
        FileScanner scanner = new(fileSystem, exclusionFilter, options.ToOptions(), NullLogger<FileScanner>.Instance);
        HashCalculator hashCalculator = new(fileSystem);
        FileWatcherCoordinator coordinator = new(
            watcherFactory,
            options.ToOptions(),
            timeProvider,
            NullLogger<FileWatcherCoordinator>.Instance);

        MarkdownIndexer indexer = new(
            scopeFactory,
            scanner,
            hashCalculator,
            fileSystem,
            coordinator,
            settings,
            options.ToOptions(),
            timeProvider,
            NullLogger<MarkdownIndexer>.Instance);

        return new IndexerTestHarness(provider, fileSystem, repository, watcherFactory, timeProvider, settings, coordinator, indexer);
    }

    public async ValueTask DisposeAsync()
    {
        await _coordinator.DisposeAsync().ConfigureAwait(false);
        Repository.Dispose();
        await _provider.DisposeAsync().ConfigureAwait(false);
    }
}
