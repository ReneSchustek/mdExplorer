using System.IO;
using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Search.Abstractions;
using MdExplorer.TagCloud.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;
using TagCloudOptions = MdExplorer.TagCloud.Options.TagCloudOptions;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Baut das Hauptfenster mit echten Unterbereichen über Attrappen der Dienste.
/// </summary>
/// <remarks>
/// Die Unterbereiche sind echt, weil eine fehlende Verbindung zwischen ihnen weder dem Übersetzer
/// noch dem Test eines einzelnen Bereichs auffällt. Das Layout schreibt der
/// <see cref="UiSettingsStore"/> in dasselbe Dateisystem im Speicher wie Editor und Ordnerbaum.
/// </remarks>
internal sealed class MainTestHarness : IDisposable
{
    /// <summary>Fester Zeitpunkt der Testuhr.</summary>
    public static readonly DateTime FixedUtc = new(2026, 6, 12, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>Zone der Prüfung: zwei Stunden vor UTC, ohne Sommerzeitsprünge.</summary>
    public static readonly TimeZoneInfo TestZone =
        TimeZoneInfo.CreateCustomTimeZone("MdExplorer-TestZone", TimeSpan.FromHours(2), "TestZone", "TestZone");

    private const string LayoutPath = @"C:\Anwendungsdaten\MdExplorer\" + UiSettingsStore.LayoutFileName;

    private readonly ServiceProvider _provider;

    public MainTestHarness()
    {
        Time.SetLocalTimeZone(TestZone);
        _provider = new ServiceCollection()
            .AddScoped<IAllFilesQuery>(_ => AllFilesQuery)
            .AddScoped<ISearchService>(_ => SearchService)
            .AddScoped<IMarkdownDocumentRepository>(_ => DocRepo)
            // Die Vorschau schlägt den Ordner der Datei nach, damit relative Bildpfade ein Zuhause haben.
            .AddScoped<IMarkdownFileRepository>(_ => FileRepo)
            .BuildServiceProvider(validateScopes: true);
        IServiceScopeFactory scopes = _provider.GetRequiredService<IServiceScopeFactory>();

        Editor = TestEditors.Create(FileSystem, Time);
        FolderTree = new FolderTreeViewModel(SettingsService, FileSystem, new FakeDialogService(), NullLogger<FolderTreeViewModel>.Instance);
        Search = new SearchViewModel(scopes, Time, Messenger, SettingsService, UiDispatcher, NullLogger<SearchViewModel>.Instance);
        DocumentPanel = CreateDocumentPanel(scopes);
        TagCloud = new TagCloudViewModel(TagStats, Messenger, MicrosoftOptions.Create(new TagCloudOptions()), NullLogger<TagCloudViewModel>.Instance);
        SettingsStore = new UiSettingsStore(FileSystem, LayoutPath, NullLogger<UiSettingsStore>.Instance);
        RecentFiles = new RecentFilesViewModel(SettingsStore, FileSystem, JumpList, NullLogger<RecentFilesViewModel>.Instance);
        Panels = new MainPanels(
            FolderTree,
            new AllFilesViewModel(scopes, Time, NullLogger<AllFilesViewModel>.Instance),
            Search,
            DocumentPanel,
            TagCloud,
            RecentFiles);
        Services = new MainServices(Locator, SettingsStore, HealthProvider, UiDispatcher, Indexer, Messenger, Time);
        Main = new MainViewModel(Panels, Services, NullLogger<MainViewModel>.Instance);
    }

    public StrongReferenceMessenger Messenger { get; } = new();
    public FakeSettingsService SettingsService { get; } = new(AppSettings.Default);
    public FakeFileSystem FileSystem { get; } = new();
    public TableDocumentLocator Locator { get; } = new();
    public FakeUnsavedChangesDialogService UnsavedChangesDialog { get; } = new();
    public ScriptedHealthProvider HealthProvider { get; } = new();
    public QueuedUiDispatcher UiDispatcher { get; } = new();
    public ListAllFilesQuery AllFilesQuery { get; } = new();
    public FakeSearchService SearchService { get; } = new();
    public FakeMarkdownDocumentRepository DocRepo { get; } = new();
    public FakeMarkdownFileRepository FileRepo { get; } = new();
    public EmptyTagStatistics TagStats { get; } = new();
    public RecordingJumpList JumpList { get; } = new();
    public RecentFilesViewModel RecentFiles { get; }
    public FakeIndexer Indexer { get; } = new();
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(FixedUtc));

    public MarkdownEditorViewModel Editor { get; }
    public MainPanels Panels { get; }
    public MainServices Services { get; }
    public UiSettingsStore SettingsStore { get; }
    public MainViewModel Main { get; }

    public FolderTreeViewModel FolderTree { get; }
    public SearchViewModel Search { get; }
    public DocumentPanelViewModel DocumentPanel { get; }
    public TagCloudViewModel TagCloud { get; }
    public AllFilesViewModel AllFiles => Panels.AllFiles;
    public PreviewViewModel Preview => DocumentPanel.Preview;

    /// <summary>Liest das Layout so, wie es beim nächsten Start gelesen würde.</summary>
    public UiLayout ReadPersistedLayout() =>
        new UiSettingsStore(FileSystem, LayoutPath, NullLogger<UiSettingsStore>.Instance).Load();

    /// <summary>
    /// Gibt alles frei, wie es der Container der Anwendung täte: Hauptfenster und Bereiche
    /// getrennt, denn das Hauptfenster besitzt die Bereiche nicht.
    /// </summary>
    public void Dispose()
    {
        Main.Dispose();
        DocumentPanel.Dispose();
        Search.Dispose();
        FolderTree.Dispose();
        TagCloud.Dispose();
        Editor.Dispose();
        _provider.Dispose();
    }

    private DocumentPanelViewModel CreateDocumentPanel(IServiceScopeFactory scopes)
    {
        PreviewHtmlBuilder builder = new(new FakeThemeProvider(isDarkMode: false), new FakeSettingsService(AppSettings.Default));
        PreviewViewModel preview = new(scopes, builder, NullLogger<PreviewViewModel>.Instance);
        DocumentRelationsViewModel relations = new(
            scopes,
            new StubDocumentFileService(),
            new FakeDialogService(),
            new StubUnsavedChangesGuard(),
            NullLogger<DocumentRelationsViewModel>.Instance);
        return new DocumentPanelViewModel(
            preview,
            Editor,
            relations,
            new EmptyMarkdownParser(),
            Locator,
            FileSystem,
            new UnsavedChangesGuard(Editor, UnsavedChangesDialog),
            NullLogger<DocumentPanelViewModel>.Instance);
    }
}
