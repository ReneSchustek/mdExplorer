using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Text;
using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.Messaging;
using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Graph.Models;
using MdExplorer.Indexer.Abstractions;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.Models;
using MdExplorer.Parser.Services;
using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;
using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.Models;
using MdExplorer.TagCloud.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;
using TagCloudOptions = MdExplorer.TagCloud.Options.TagCloudOptions;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>Unit-Tests des <see cref="MainViewModel"/> mit produktiven Child-ViewModels.</summary>
public sealed class MainViewModelTests
{
    /// <summary>Der Registerindex des Suchbereichs — dieselbe Zahl wie im ViewModel.</summary>
    private const int SearchTabIndex = 2;

    private static readonly DateTime FixedUtc = new(2026, 6, 12, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>Zone der Prüfung: zwei Stunden vor UTC, ohne Sommerzeitsprünge.</summary>
    private static readonly TimeZoneInfo TestZone =
        TimeZoneInfo.CreateCustomTimeZone("MdExplorer-TestZone", TimeSpan.FromHours(2), "TestZone", "TestZone");

    [Fact]
    public void Construction_SubscribesToChildEvents()
    {
        using TestHarness harness = new();

        harness.FolderTree.SelectedPathPrefix = "/wurzel";

        Assert.Equal("/wurzel", harness.Search.PathPrefixFilter);
    }

    [Fact]
    public void Dispose_UnsubscribesFromAllChildEvents_AndDisposesChildren()
    {
        using TestHarness harness = new();
        harness.FolderTree.SelectedPathPrefix = "/initial";
        Assert.Equal("/initial", harness.Search.PathPrefixFilter);

        harness.Main.Dispose();

        // FolderTree-PropertyChanged ist deabonniert — Search.PathPrefixFilter wird nicht mehr durchgereicht.
        harness.FolderTree.SelectedPathPrefix = "/danach";
        Assert.Equal("/initial", harness.Search.PathPrefixFilter);

        // Health-Provider-Changed ist deabonniert.
        OperationHealth before = harness.Main.Health;
        harness.HealthProvider.SetState(OperationHealth.Error, "Kritisch");
        Assert.Equal(before, harness.Main.Health);
    }

    [Fact]
    public async Task NavigateToWikiLinkAsync_OnResolvedTarget_LoadsDocumentAndSelectsResult()
    {
        using TestHarness harness = new();
        Guid targetId = Guid.NewGuid();
        harness.Locator.SetWikiLink("ziel", targetId);
        harness.DocRepo.Put(targetId, CreateDocument(targetId, "<h1>Ziel</h1>"));
        harness.Search.Results.Add(new SearchResultItemViewModel(
            new SearchResult(targetId, "ziel.md", "Ziel", 0.0, "<p>snippet</p>", Array.Empty<SearchHighlight>())));

        bool result = await harness.Main.NavigateToWikiLinkAsync("ziel", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result);
        Assert.Equal(targetId, harness.Preview.CurrentDocumentId);
        Assert.NotNull(harness.Search.SelectedResult);
        Assert.Equal(targetId, harness.Search.SelectedResult!.MarkdownFileId);
    }

    [Fact]
    public async Task NavigateToWikiLinkAsync_OnUnresolvedTarget_LogsAndReturnsFalse()
    {
        using TestHarness harness = new();

        bool result = await harness.Main.NavigateToWikiLinkAsync("ungelöst", CancellationToken.None).ConfigureAwait(true);

        Assert.False(result);
        Assert.Null(harness.Preview.CurrentDocumentId);
        Assert.Null(harness.Search.SelectedResult);
    }

    [Fact]
    public void OnIndexerProgress_UpdatesIndexedFileCount_AndLastRunUtc_OverDispatcher()
    {
        using TestHarness harness = new();
        Assert.Equal(0, harness.Main.IndexedFileCount);
        Assert.Null(harness.Main.LastIndexerRunUtc);
        int dispatcherCallsBefore = harness.UiDispatcher.InvokeCount;

        harness.Indexer.RaiseProgress("F:/root-a", 17, isCompleted: false);

        Assert.Equal(17, harness.Main.IndexedFileCount);
        Assert.Equal(FixedUtc, harness.Main.LastIndexerRunUtc);
        Assert.True(harness.UiDispatcher.InvokeCount > dispatcherCallsBefore);

        harness.Indexer.RaiseProgress("F:/root-b", 5, isCompleted: false);

        Assert.Equal(22, harness.Main.IndexedFileCount);

        // Re-Emission der gleichen Root überschreibt, sie wird nicht aufaddiert.
        harness.Indexer.RaiseProgress("F:/root-a", 30, isCompleted: true);

        Assert.Equal(35, harness.Main.IndexedFileCount);
    }

    [Fact]
    public void LastIndexerRun_IsShownInTheLocalTimeZone()
    {
        // In der Statusleiste stand bisher UTC. Wer nachsieht, wann zuletzt indiziert
        // wurde, vergleicht das aber mit seiner eigenen Uhr.
        using TestHarness harness = new();
        List<string> changed = [];
        harness.Main.PropertyChanged += (_, args) => changed.Add(args.PropertyName ?? string.Empty);
        Assert.Null(harness.Main.LastIndexerRun);

        harness.Indexer.RaiseProgress("F:/root-a", 3, isCompleted: true);

        Assert.Equal(TimeZoneInfo.ConvertTimeFromUtc(FixedUtc, TestZone), harness.Main.LastIndexerRun);
        Assert.NotEqual(harness.Main.LastIndexerRunUtc, harness.Main.LastIndexerRun);
        Assert.Contains(nameof(MainViewModel.LastIndexerRun), changed, StringComparer.Ordinal);
    }

    [Fact]
    public void OnHealthChanged_PropagatesToObservableProperties_OverDispatcher()
    {
        using TestHarness harness = new();
        int dispatcherCallsBefore = harness.UiDispatcher.InvokeCount;

        harness.HealthProvider.SetState(OperationHealth.Warning, "Indexer hängt.");

        Assert.Equal(OperationHealth.Warning, harness.Main.Health);
        Assert.Equal("Indexer hängt.", harness.Main.HealthDetail);
        Assert.True(harness.UiDispatcher.InvokeCount > dispatcherCallsBefore, "OnHealthChanged muss über den UI-Dispatcher marshalen.");
    }

    [Fact]
    public void ToggleTagCloud_FlipsVisibilityAndPersistsState()
    {
        using TestHarness harness = new();
        bool initial = harness.Main.IsTagCloudVisible;

        harness.Main.ToggleTagCloudCommand.Execute(parameter: null);

        Assert.NotEqual(initial, harness.Main.IsTagCloudVisible);

        UiSettingsStore reread = new(harness.SettingsStore.StorageLocation, NullLogger<UiSettingsStore>.Instance);
        UiLayout persisted = reread.Load();
        Assert.Equal(harness.Main.IsTagCloudVisible, persisted.IsTagCloudVisible);

        harness.Main.ToggleTagCloudCommand.Execute(parameter: null);

        Assert.Equal(initial, harness.Main.IsTagCloudVisible);
        UiLayout persistedAfterSecond = new UiSettingsStore(harness.SettingsStore.StorageLocation, NullLogger<UiSettingsStore>.Instance).Load();
        Assert.Equal(initial, persistedAfterSecond.IsTagCloudVisible);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "Die FolderTree-/Search-/TagCloud-/DocumentPanel-VMs werden von MainViewModel.Dispose() über das using freigegeben.")]
    public void LeftTabIndex_Change_PersistsToSettings()
    {
        using TestHarness harness = new();
        Assert.Equal(0, harness.Main.LeftTabIndex);

        harness.Main.LeftTabIndex = 2;

        UiLayout persisted = new UiSettingsStore(harness.SettingsStore.StorageLocation, NullLogger<UiSettingsStore>.Instance).Load();
        Assert.Equal(2, persisted.LeftTabIndex);

        // Eine zweite MainViewModel-Instanz auf demselben Store liest den Stand zurück.
        using ServiceProvider freshProvider = new ServiceCollection()
            .AddScoped<IAllFilesQuery>(_ => harness.AllFilesQuery)
            .AddScoped<ISearchService>(_ => harness.SearchService)
            .AddScoped<IMarkdownDocumentRepository>(_ => harness.DocRepo)
            .BuildServiceProvider(validateScopes: true);
        FolderTreeViewModel folderTree = new(harness.SettingsService, harness.FileSystem);
        AllFilesViewModel allFiles = new(freshProvider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<AllFilesViewModel>.Instance);
        SearchViewModel search = new(freshProvider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, harness.Messenger, NullLogger<SearchViewModel>.Instance);
        TagCloudViewModel tagCloud = new(harness.TagStats, harness.Messenger, MicrosoftOptions.Create(new TagCloudOptions()), NullLogger<TagCloudViewModel>.Instance);
        PreviewHtmlBuilder builder = new(new FakeThemeProvider(isDarkMode: false));
        PreviewViewModel preview = new(freshProvider.GetRequiredService<IServiceScopeFactory>(), builder, NullLogger<PreviewViewModel>.Instance);
        MarkdownEditorViewModel editor = new(harness.FileSystem, new TagExtractor(harness.SettingsService), TimeProvider.System, NullLogger<MarkdownEditorViewModel>.Instance);
        DocumentPanelViewModel documentPanel = new(preview, editor, NoRelations(), harness.Parser, builder, harness.Locator, harness.FileSystem, NullLogger<DocumentPanelViewModel>.Instance);
        using MainViewModel restored = new(folderTree, allFiles, search, documentPanel, tagCloud, harness.Locator, harness.SettingsStore, harness.HealthProvider, harness.UiDispatcher, harness.Indexer, harness.Messenger, harness.FixedTime, NullLogger<MainViewModel>.Instance);

        Assert.Equal(2, restored.LeftTabIndex);
    }

    [Fact]
    public void OnFolderTreeChanged_OnSelectedPathPrefix_UpdatesSearchPathFilter()
    {
        using TestHarness harness = new();

        harness.FolderTree.SelectedPathPrefix = "/projekt/sub";

        Assert.Equal("/projekt/sub", harness.Search.PathPrefixFilter);

        harness.FolderTree.SelectedPathPrefix = null;

        Assert.Null(harness.Search.PathPrefixFilter);
    }

    private static MarkdownDocument CreateDocument(Guid fileId, string body)
    {
        MarkdownDocument document = new()
        {
            Id = Guid.NewGuid(),
            MarkdownFileId = fileId,
            SourceContentHash = "hash",
            FrontmatterJson = "{}",
            OutlinksJson = "[]",
            ParsedAtUtc = FixedUtc,
        };
        document.SetRenderedHtmlGz(Gzip(body));
        return document;
    }

    private static byte[] Gzip(string text)
    {
        using MemoryStream output = new();
        using (GZipStream gz = new(output, CompressionLevel.Fastest))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            gz.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }

    [Fact]
    public void Receive_OnUpdateAvailable_ShowsTheHintBarOverTheDispatcher()
    {
        // Die Nachricht kommt aus dem Hintergrunddienst und damit von einem fremden Thread.
        // Ohne den Umweg über den Dispatcher würde das Setzen der Bindungen die Oberfläche werfen.
        using TestHarness harness = new();
        Uri releaseUrl = new("https://example.invalid/release");

        harness.Main.Receive(new UpdateAvailableMessage("1.2.3", releaseUrl));

        Assert.True(harness.Main.IsUpdateAvailable);
        Assert.Equal("1.2.3", harness.Main.UpdateVersion, StringComparer.Ordinal);
        Assert.Equal(releaseUrl, harness.Main.UpdateReleaseUrl);
        Assert.True(harness.UiDispatcher.InvokeCount > 0);
    }

    [Fact]
    public void Receive_ViaMessenger_ReachesTheViewModel()
    {
        using TestHarness harness = new();

        _ = harness.Messenger.Send(new UpdateAvailableMessage("2.0.0", new Uri("https://example.invalid/r")));

        Assert.True(harness.Main.IsUpdateAvailable);
    }

    [Fact]
    public void Receive_WithoutMessage_Throws()
    {
        using TestHarness harness = new();

        _ = Assert.Throws<ArgumentNullException>(() => harness.Main.Receive(null!));
    }

    [Fact]
    public void DismissUpdate_HidesTheHintBar()
    {
        using TestHarness harness = new();
        harness.Main.Receive(new UpdateAvailableMessage("1.2.3", new Uri("https://example.invalid/r")));

        harness.Main.DismissUpdateCommand.Execute(null);

        Assert.False(harness.Main.IsUpdateAvailable);
    }

    [Fact]
    public void Title_AndStorageLocation_AreAvailableForTheStatusBar()
    {
        using TestHarness harness = new();

        Assert.Equal("MdExplorer", harness.Main.Title, StringComparer.Ordinal);
        Assert.Equal(harness.SettingsStore.StorageLocation, harness.Main.StorageLocation, StringComparer.Ordinal);
    }

    [Fact]
    public async Task NavigateToDocumentAsync_WithoutAFile_ReportsFailure()
    {
        using TestHarness harness = new();

        bool ergebnis = await harness.Main.NavigateToDocumentAsync(Guid.Empty, CancellationToken.None).ConfigureAwait(true);

        Assert.False(ergebnis);
    }

    [Fact]
    public void SelectedResult_ClearedAgain_DoesNotReloadTheDocument()
    {
        using TestHarness harness = new();

        harness.Search.SelectedResult = null;

        Assert.Null(harness.Preview.CurrentDocumentId);
    }

    [Fact]
    public async Task SelectingAFileInTheAllFilesTab_LoadsTheIndexedDocument()
    {
        using TestHarness harness = new();
        Guid fileId = Guid.NewGuid();
        const string Pfad = @"C:\notizen\bericht.md";
        harness.Locator.SetAbsolutePath(Pfad, fileId);
        harness.DocRepo.Put(fileId, CreateDocument(fileId, "<h1>Bericht</h1>"));

        harness.AllFiles.SelectedItem = new AllFilesItemViewModel(
            new AllFilesRow(fileId, "Bericht", @"notizen\bericht.md", Pfad, FixedUtc, []), TimeZoneInfo.Utc);

        Assert.True(
            await WaitForAsync(() => harness.Preview.CurrentDocumentId == fileId).ConfigureAwait(true),
            "Die ausgewählte Datei wurde nicht geladen.");
    }

    [Fact]
    public async Task SelectingAFileThatIsNotIndexedYet_FallsBackToTheDirectLoad()
    {
        // Direkt nach dem Anlegen kennt der Indexer die Datei noch nicht. Ohne diesen
        // Ausweichpfad bliebe die Vorschau leer, bis der nächste Scan durchgelaufen ist.
        using TestHarness harness = new();
        const string Pfad = @"C:\notizen\ganz-neu.md";
        harness.FileSystem.Files[Pfad] = Encoding.UTF8.GetBytes("# Ganz neu");

        harness.AllFiles.SelectedItem = new AllFilesItemViewModel(
            new AllFilesRow(Guid.NewGuid(), "Ganz neu", @"notizen\ganz-neu.md", Pfad, FixedUtc, []), TimeZoneInfo.Utc);

        Assert.True(
            await WaitForAsync(() => harness.Locator.AbsolutePathCallCount > 0).ConfigureAwait(true),
            "Die Pfad-Auflösung wurde nicht versucht.");
    }

    [Fact]
    public async Task SelectingAFileWhenTheDatabaseIsBusy_DoesNotCrashTheApplication()
    {
        using TestHarness harness = new();
        harness.Locator.FailOnAbsolutePath = new TestDbException("Datenbank belegt");

        harness.AllFiles.SelectedItem = new AllFilesItemViewModel(
            new AllFilesRow(Guid.NewGuid(), "Bericht", @"notizen\b.md", @"C:\notizen\b.md", FixedUtc, []), TimeZoneInfo.Utc);

        Assert.True(
            await WaitForAsync(() => harness.Locator.AbsolutePathCallCount > 0).ConfigureAwait(true),
            "Die Pfad-Auflösung wurde nicht versucht.");
        Assert.Null(harness.Preview.CurrentDocumentId);
    }

    /// <summary>
    /// Wartet begrenzt auf eine Bedingung. Die Navigation nach einem Klick läuft absichtlich
    /// ohne Rückgabewert los, deshalb gibt es keinen Task, auf den der Test warten könnte.
    /// </summary>
    private static async Task<bool> WaitForAsync(Func<bool> bedingung)
    {
        DateTimeOffset ende = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < ende)
        {
            if (bedingung())
            {
                return true;
            }
            await Task.Delay(5).ConfigureAwait(false);
        }
        return bedingung();
    }

    private sealed class TestHarness : IDisposable
    {
        public StrongReferenceMessenger Messenger { get; } = new();
        public StubSettingsService SettingsService { get; } = new();
        public FakeFileSystem FileSystem { get; } = new();
        public FakeDocumentLocator Locator { get; } = new();
        public FakeOperationHealthProvider HealthProvider { get; } = new();
        public ImmediateUiDispatcher UiDispatcher { get; } = new();
        public FakeAllFilesQuery AllFilesQuery { get; } = new();
        public FakeSearchService SearchService { get; } = new();
        public FakeMarkdownDocumentRepository DocRepo { get; } = new();
        public FakeTagStatistics TagStats { get; } = new();
        public FakeMarkdownParser Parser { get; } = new();
        public FakeIndexer Indexer { get; } = new();
        public FakeTimeProvider FixedTime { get; } = new(FixedUtc);

        public FolderTreeViewModel FolderTree { get; }
        public AllFilesViewModel AllFiles { get; }
        public SearchViewModel Search { get; }
        public TagCloudViewModel TagCloud { get; }
        public PreviewViewModel Preview { get; }
        public MarkdownEditorViewModel Editor { get; }
        public DocumentPanelViewModel DocumentPanel { get; }
        public MainViewModel Main { get; }
        public ServiceProvider Provider { get; }
        public UiSettingsStore SettingsStore { get; }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Editor und Provider werden vom TestHarness-Dispose freigegeben (Main.Dispose() entsorgt seine Children).")]
        public TestHarness()
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IAllFilesQuery>(_ => AllFilesQuery);
            _ = services.AddScoped<ISearchService>(_ => SearchService);
            _ = services.AddScoped<IMarkdownDocumentRepository>(_ => DocRepo);
            Provider = services.BuildServiceProvider(validateScopes: true);

            FolderTree = new FolderTreeViewModel(SettingsService, FileSystem);
            AllFiles = new AllFilesViewModel(
                Provider.GetRequiredService<IServiceScopeFactory>(),
                TimeProvider.System,
                NullLogger<AllFilesViewModel>.Instance);
            Search = new SearchViewModel(
                Provider.GetRequiredService<IServiceScopeFactory>(),
                TimeProvider.System,
                Messenger,
                NullLogger<SearchViewModel>.Instance);
            TagCloud = new TagCloudViewModel(
                TagStats,
                Messenger,
                MicrosoftOptions.Create(new TagCloudOptions()),
                NullLogger<TagCloudViewModel>.Instance);
            PreviewHtmlBuilder builder = new(new FakeThemeProvider(isDarkMode: false));
            Preview = new PreviewViewModel(
                Provider.GetRequiredService<IServiceScopeFactory>(),
                builder,
                NullLogger<PreviewViewModel>.Instance);
            Editor = new MarkdownEditorViewModel(
                FileSystem,
                new TagExtractor(SettingsService),
                TimeProvider.System,
                NullLogger<MarkdownEditorViewModel>.Instance);
            DocumentPanel = new DocumentPanelViewModel(
                Preview,
                Editor,
                NoRelations(),
                Parser,
                builder,
                Locator,
                FileSystem,
                NullLogger<DocumentPanelViewModel>.Instance);
            SettingsStore = new UiSettingsStore(
                Path.Combine(Path.GetTempPath(), $"mdexplorer-ui-{Guid.NewGuid():N}.json"),
                NullLogger<UiSettingsStore>.Instance);
            Main = new MainViewModel(
                FolderTree,
                AllFiles,
                Search,
                DocumentPanel,
                TagCloud,
                Locator,
                SettingsStore,
                HealthProvider,
                UiDispatcher,
                Indexer,
                Messenger,
                FixedTime,
                NullLogger<MainViewModel>.Instance);
        }

        public void Dispose()
        {
            Main.Dispose();
            Provider.Dispose();
        }
    }

    private sealed class FakeDocumentLocator : IDocumentLocator
    {
        private readonly Dictionary<string, Guid> _wikiLinks = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid> _absolutePaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Guid, string> _idToPath = [];

        /// <summary>
        /// Fehler, den die Pfad-Auflösung statt eines Ergebnisses liefert. Nötig, um den
        /// Ausweichpfad bei einer Datenbank-Spitze zu prüfen.
        /// </summary>
        public Exception? FailOnAbsolutePath { get; set; }

        /// <summary>Zählt die Pfad-Auflösungen — die Navigation läuft ohne Rückgabewert.</summary>
        public int AbsolutePathCallCount { get; private set; }

        public void SetWikiLink(string target, Guid id) => _wikiLinks[target] = id;

        public void SetAbsolutePath(string absolutePath, Guid id)
        {
            _absolutePaths[absolutePath] = id;
            _idToPath[id] = absolutePath;
        }

        public Task<Guid?> FindByWikiLinkAsync(string wikiLinkTarget, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(_wikiLinks.TryGetValue(wikiLinkTarget, out Guid id) ? id : null);

        public Task<Guid?> FindByAbsolutePathAsync(string absoluteFilePath, CancellationToken cancellationToken)
        {
            AbsolutePathCallCount++;
            return FailOnAbsolutePath is not null
                ? Task.FromException<Guid?>(FailOnAbsolutePath)
                : Task.FromResult<Guid?>(_absolutePaths.TryGetValue(absoluteFilePath, out Guid id) ? id : null);
        }


        public Task<string?> GetAbsolutePathAsync(Guid markdownFileId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(_idToPath.TryGetValue(markdownFileId, out string? path) ? path : null);
    }

    private sealed class FakeIndexer : IIndexer
    {
        public event EventHandler<IndexerScanProgressEventArgs>? InitialScanProgress;

        public Task RunInitialScanAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void RaiseProgress(string root, int processedCount, bool isCompleted) =>
            InitialScanProgress?.Invoke(this, new IndexerScanProgressEventArgs(root, processedCount, isCompleted));
    }

    private sealed class FakeTimeProvider(DateTime initialUtc) : TimeProvider
    {
        private DateTimeOffset _now = new(DateTime.SpecifyKind(initialUtc, DateTimeKind.Utc), TimeSpan.Zero);

        /// <summary>
        /// Feste Zone statt der des Prüfrechners — sonst hinge das Ergebnis davon ab,
        /// wo die Suite läuft.
        /// </summary>
        public override TimeZoneInfo LocalTimeZone => TestZone;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }

    private sealed class FakeOperationHealthProvider : IOperationHealthProvider
    {
        public OperationHealth Current { get; private set; } = OperationHealth.Healthy;

        public string Detail { get; private set; } = "Alle Subsysteme laufen normal.";

        public event EventHandler? Changed;

        public void SetState(OperationHealth state, string detail)
        {
            Current = state;
            Detail = detail;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public int InvokeCount { get; private set; }

        public void Invoke(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            InvokeCount++;
            action();
        }
    }

    private sealed class FakeAllFilesQuery : IAllFilesQuery
    {
        public Task<IReadOnlyList<AllFilesRow>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AllFilesRow>>([]);
    }

    private sealed class FakeTagStatistics : ITagStatisticsService
    {
        public Task<IReadOnlyList<TagStatistic>> GetTopTagsAsync(int topN, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TagStatistic>>([]);
    }

    private sealed class FakeMarkdownParser : IMarkdownParser
    {
        public ParseResult Parse(string markdownText) =>
            new(
                new Dictionary<string, string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                ReadOnlyMemory<byte>.Empty);
    }

    private sealed class StubSettingsService : ISettingsService
    {
        public AppSettings Current { get; private set; } = AppSettings.Default;
        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            AppSettings previous = Current;
            Current = settings;
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(previous, settings));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Ein Zusammenhangs-Bereich ohne Datenquelle: Diese Tests prüfen die Verdrahtung der
    /// Spalten, nicht die Verbindungen eines Dokuments — die haben eigene Tests.
    /// </summary>
    private static DocumentRelationsViewModel NoRelations()
    {
        ServiceCollection services = new();
        ServiceProvider provider = services.BuildServiceProvider();
        return new DocumentRelationsViewModel(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new StubDocumentFileService(),
            new FakeDialogService(),
            NullLogger<DocumentRelationsViewModel>.Instance);
    }
    /// <summary>
    /// Die Verdrahtung zwischen den Bereichen — geprüft, nicht angenommen.
    /// </summary>
    /// <remarks>
    /// Sechs Ereignisse verbinden die Unterbereiche mit dem Hauptfenster. Sie sind je zwei
    /// Zeilen lang und standen bis zum 16.08.2026 ohne Prüfung da. Genau solche Zeilen fallen
    /// bei einem Umbau still weg: Der Bau bleibt grün, der Klick tut nur nichts mehr.
    /// </remarks>
    [Fact]
    public void ShowFolder_PutsThePathFilterIntoTheSearchTab()
    {
        using TestHarness harness = new();
        harness.DocumentPanel.Relations.FolderPath = "projekt/unterordner";

        harness.DocumentPanel.Relations.ShowFolderCommand.Execute(null);

        Assert.Equal("path:projekt/unterordner", harness.Search.QueryText);
        Assert.Equal(SearchTabIndex, harness.Main.LeftTabIndex);
    }

    /// <remarks>
    /// Die Gegenprobe: Ohne Ordner gibt es nichts zu zeigen. Der Befehl darf dann nicht in
    /// den Suchbereich springen und dort eine leere Einschränkung hinterlassen.
    /// </remarks>
    [Fact]
    public void ShowFolder_WithoutAFolder_ChangesNothing()
    {
        using TestHarness harness = new();
        int vorher = harness.Main.LeftTabIndex;

        harness.DocumentPanel.Relations.ShowFolderCommand.Execute(null);

        Assert.Empty(harness.Search.QueryText);
        Assert.Equal(vorher, harness.Main.LeftTabIndex);
    }

    [Fact]
    public void ShowTag_PutsTheTagFilterIntoTheSearchTab()
    {
        using TestHarness harness = new();

        harness.DocumentPanel.Relations.ShowTagCommand.Execute("architektur");

        Assert.Equal("tag:architektur", harness.Search.QueryText);
        Assert.Equal(SearchTabIndex, harness.Main.LeftTabIndex);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShowTag_WithoutATag_ChangesNothing(string? leer)
    {
        using TestHarness harness = new();
        int vorher = harness.Main.LeftTabIndex;

        harness.DocumentPanel.Relations.ShowTagCommand.Execute(leer);

        Assert.Empty(harness.Search.QueryText);
        Assert.Equal(vorher, harness.Main.LeftTabIndex);
    }

    /// <remarks>
    /// Ein Klick auf ein verwandtes Dokument öffnet es. Der Nachweis führt über den
    /// Pfad-Auflöser: Er wird genau dann gefragt, wenn das Öffnen wirklich losläuft.
    /// </remarks>
    [Fact]
    public async Task OpenRelated_OpensTheDocumentBehindTheEntry()
    {
        using TestHarness harness = new();
        Guid ziel = Guid.Parse("77777777-7777-7777-7777-777777777777");
        const string Pfad = @"C:
otizen\ziel.md";
        harness.Locator.SetAbsolutePath(Pfad, ziel);
        harness.DocRepo.Put(ziel, CreateDocument(ziel, "<h1>Ziel</h1>"));
        RelatedDocumentViewModel eintrag = new(new RelatedDocument(ziel, "ziel", "ziel.md"));

        harness.DocumentPanel.Relations.OpenRelatedCommand.Execute(eintrag);

        Assert.True(
            await WaitForAsync(() => harness.Preview.CurrentDocumentId == ziel).ConfigureAwait(true),
            "Das verwandte Dokument wurde nicht geöffnet.");
    }

    /// <remarks>
    /// Ohne Eintrag — der Befehl kann mit <see langword="null"/> ausgelöst werden, wenn die
    /// Liste gerade leer ist — darf nichts geöffnet werden.
    /// </remarks>
    [Fact]
    public void OpenRelated_WithoutAnEntry_OpensNothing()
    {
        using TestHarness harness = new();

        harness.DocumentPanel.Relations.OpenRelatedCommand.Execute(null);

        Assert.Equal(0, harness.Locator.AbsolutePathCallCount);
    }
}
