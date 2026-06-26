using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Text;
using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
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
    private static readonly DateTime FixedUtc = new(2026, 6, 12, 8, 0, 0, DateTimeKind.Utc);

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

        bool result = await harness.Main.NavigateToWikiLinkAsync("ungeloest", CancellationToken.None).ConfigureAwait(true);

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

        // Re-Emission der gleichen Root ueberschreibt, sie wird nicht aufaddiert.
        harness.Indexer.RaiseProgress("F:/root-a", 30, isCompleted: true);

        Assert.Equal(35, harness.Main.IndexedFileCount);
    }

    [Fact]
    public void OnHealthChanged_PropagatesToObservableProperties_OverDispatcher()
    {
        using TestHarness harness = new();
        int dispatcherCallsBefore = harness.UiDispatcher.InvokeCount;

        harness.HealthProvider.SetState(OperationHealth.Warning, "Indexer haengt.");

        Assert.Equal(OperationHealth.Warning, harness.Main.Health);
        Assert.Equal("Indexer haengt.", harness.Main.HealthDetail);
        Assert.True(harness.UiDispatcher.InvokeCount > dispatcherCallsBefore, "OnHealthChanged muss ueber den UI-Dispatcher marshalen.");
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
        Justification = "Die FolderTree-/Search-/TagCloud-/DocumentPanel-VMs werden von MainViewModel.Dispose() ueber das using freigegeben.")]
    public void LeftTabIndex_Change_PersistsToSettings()
    {
        using TestHarness harness = new();
        Assert.Equal(0, harness.Main.LeftTabIndex);

        harness.Main.LeftTabIndex = 2;

        UiLayout persisted = new UiSettingsStore(harness.SettingsStore.StorageLocation, NullLogger<UiSettingsStore>.Instance).Load();
        Assert.Equal(2, persisted.LeftTabIndex);

        // Eine zweite MainViewModel-Instanz auf demselben Store liest den Stand zurueck.
        using ServiceProvider freshProvider = new ServiceCollection()
            .AddScoped<IAllFilesQuery>(_ => harness.AllFilesQuery)
            .AddScoped<ISearchService>(_ => harness.SearchService)
            .AddScoped<IMarkdownDocumentRepository>(_ => harness.DocRepo)
            .BuildServiceProvider(validateScopes: true);
        FolderTreeViewModel folderTree = new(harness.SettingsService, harness.FileSystem);
        AllFilesViewModel allFiles = new(freshProvider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AllFilesViewModel>.Instance);
        SearchViewModel search = new(freshProvider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, harness.Messenger, NullLogger<SearchViewModel>.Instance);
        TagCloudViewModel tagCloud = new(harness.TagStats, harness.Messenger, MicrosoftOptions.Create(new TagCloudOptions()), NullLogger<TagCloudViewModel>.Instance);
        PreviewHtmlBuilder builder = new(new FakeThemeProvider(isDarkMode: false));
        PreviewViewModel preview = new(freshProvider.GetRequiredService<IServiceScopeFactory>(), builder, NullLogger<PreviewViewModel>.Instance);
        MarkdownEditorViewModel editor = new(harness.FileSystem, new TagExtractor(harness.SettingsService), TimeProvider.System, NullLogger<MarkdownEditorViewModel>.Instance);
        DocumentPanelViewModel documentPanel = new(preview, editor, harness.Parser, builder, harness.Locator, harness.FileSystem, NullLogger<DocumentPanelViewModel>.Instance);
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

        public void SetWikiLink(string target, Guid id) => _wikiLinks[target] = id;

        public void SetAbsolutePath(string absolutePath, Guid id)
        {
            _absolutePaths[absolutePath] = id;
            _idToPath[id] = absolutePath;
        }

        public Task<Guid?> FindByWikiLinkAsync(string wikiLinkTarget, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(_wikiLinks.TryGetValue(wikiLinkTarget, out Guid id) ? id : null);

        public Task<Guid?> FindByAbsolutePathAsync(string absoluteFilePath, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(_absolutePaths.TryGetValue(absoluteFilePath, out Guid id) ? id : null);

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
}
