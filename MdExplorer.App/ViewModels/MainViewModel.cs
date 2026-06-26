using System.Data.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.Messaging;
using MdExplorer.App.Services;
using MdExplorer.Core.Abstractions;
using MdExplorer.Indexer.Abstractions;
using MdExplorer.TagCloud.ViewModels;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Orchestriert die drei Spalten-ViewModels. Verdrahtet Auswahl-Events
/// (Tree → Suchfilter, Suchtreffer → Preview) und liefert die persistierten Spaltenbreiten.
/// </summary>
internal sealed partial class MainViewModel : ObservableObject, INavigationService, IDisposable,
    IRecipient<UpdateAvailableMessage>
{
    private readonly IDocumentLocator _documentLocator;
    private readonly UiSettingsStore _settingsStore;
    private readonly IOperationHealthProvider _healthProvider;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IIndexer _indexer;
    private readonly IMessenger _messenger;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MainViewModel> _logger;
    private readonly Dictionary<string, int> _perRootProcessed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _perRootLock = new();
    private bool _disposed;

    [ObservableProperty]
    private double _folderColumnWidth;

    [ObservableProperty]
    private double _resultColumnWidth;

    [ObservableProperty]
    private double _previewColumnWidth;

    [ObservableProperty]
    private int _indexedFileCount;

    [ObservableProperty]
    private DateTime? _lastIndexerRunUtc;

    [ObservableProperty]
    private bool _isTagCloudVisible;

    [ObservableProperty]
    private int _leftTabIndex;

    [ObservableProperty]
    private OperationHealth _health = OperationHealth.Healthy;

    [ObservableProperty]
    private string _healthDetail = "Alle Subsysteme laufen normal.";

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string _updateVersion = string.Empty;

    [ObservableProperty]
    private Uri? _updateReleaseUrl;

    /// <summary>Erzeugt das ViewModel und verbindet die Child-ViewModels.</summary>
    public MainViewModel(
        FolderTreeViewModel folderTree,
        AllFilesViewModel allFiles,
        SearchViewModel search,
        DocumentPanelViewModel documentPanel,
        TagCloudViewModel tagCloud,
        IDocumentLocator documentLocator,
        UiSettingsStore settingsStore,
        IOperationHealthProvider healthProvider,
        IUiDispatcher uiDispatcher,
        IIndexer indexer,
        IMessenger messenger,
        TimeProvider timeProvider,
        ILogger<MainViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(folderTree);
        ArgumentNullException.ThrowIfNull(allFiles);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(documentPanel);
        ArgumentNullException.ThrowIfNull(tagCloud);
        ArgumentNullException.ThrowIfNull(documentLocator);
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(healthProvider);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(indexer);
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        FolderTree = folderTree;
        AllFiles = allFiles;
        Search = search;
        DocumentPanel = documentPanel;
        TagCloud = tagCloud;
        _documentLocator = documentLocator;
        _settingsStore = settingsStore;
        _healthProvider = healthProvider;
        _uiDispatcher = uiDispatcher;
        _indexer = indexer;
        _messenger = messenger;
        _timeProvider = timeProvider;
        _logger = logger;
        StorageLocation = settingsStore.StorageLocation;
        Health = healthProvider.Current;
        HealthDetail = healthProvider.Detail;
        healthProvider.Changed += OnHealthChanged;
        indexer.InitialScanProgress += OnIndexerProgress;

        UiLayout layout = settingsStore.Load();
        _folderColumnWidth = layout.FolderColumnWidth;
        _resultColumnWidth = layout.ResultColumnWidth;
        _previewColumnWidth = layout.PreviewColumnWidth;
        _isTagCloudVisible = layout.IsTagCloudVisible;
        _leftTabIndex = layout.LeftTabIndex;

        FolderTree.PropertyChanged += OnFolderTreeChanged;
        FolderTree.FileSelected += OnFolderTreeFileSelected;
        AllFiles.FileSelected += OnAllFilesFileSelected;
        Search.PropertyChanged += OnSearchChanged;

        _messenger.RegisterAll(this);

        LogCreated(logger);
    }

    /// <summary>
    /// Empfängt die Update-Benachrichtigung des Hintergrunddienstes (auf einem ThreadPool-Thread)
    /// und blendet die Hinweisleiste ein. Das Setzen der Bindings wird über den
    /// <see cref="IUiDispatcher"/> auf den UI-Thread marshalled.
    /// </summary>
    public void Receive(UpdateAvailableMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _uiDispatcher.Invoke(() =>
        {
            UpdateVersion = message.Version;
            UpdateReleaseUrl = message.ReleaseUrl;
            IsUpdateAvailable = true;
        });
    }

    /// <summary>Schließt die Update-Hinweisleiste, ohne die Release-Seite zu öffnen.</summary>
    [RelayCommand]
    private void DismissUpdate() => IsUpdateAvailable = false;

    /// <summary>Linke Spalte, Tab 1: Ordnerbaum.</summary>
    public FolderTreeViewModel FolderTree { get; }

    /// <summary>Linke Spalte, Tab 2: flache Liste aller indizierten Dateien.</summary>
    public AllFilesViewModel AllFiles { get; }

    /// <summary>Mittlere Spalte.</summary>
    public SearchViewModel Search { get; }

    /// <summary>Rechte Spalte (Read/Edit-Container).</summary>
    public DocumentPanelViewModel DocumentPanel { get; }

    /// <summary>Tag-Cloud-Panel (zuschaltbar).</summary>
    public TagCloudViewModel TagCloud { get; }

    /// <summary>Titelzeile des Hauptfensters.</summary>
#pragma warning disable S2325 // WPF-Binding erwartet Instanz-Property auf dem DataContext.
    public string Title => "MdExplorer";
#pragma warning restore S2325

    /// <summary>Pfad-Anzeige in der Statusleiste — Speicherort der Settings/Datenbank.</summary>
    public string StorageLocation { get; }

    /// <summary>Persistiert die aktuellen Spaltenbreiten, Tag-Cloud-Sichtbarkeit und Left-Tab-Index.</summary>
    public void PersistColumnLayout()
    {
        _settingsStore.Save(new UiLayout(
            FolderColumnWidth,
            ResultColumnWidth,
            PreviewColumnWidth,
            HelpWindow: null,
            IsTagCloudVisible: IsTagCloudVisible,
            LeftTabIndex: LeftTabIndex));
    }

    /// <summary>Schaltet das Tag-Cloud-Panel ein/aus. Hot-Save erfolgt im <see cref="OnIsTagCloudVisibleChanged"/>-Hook.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void ToggleTagCloud() => IsTagCloudVisible = !IsTagCloudVisible;

    /// <summary>
    /// Hot-Save. Jeder Wechsel der Sichtbarkeit (Command, Menue-TwoWay-Binding,
    /// Ctrl+T) wird sofort persistiert, damit der Stand beim naechsten Start auch dann erhalten
    /// bleibt, wenn das Hauptfenster ungewoehnlich beendet wird (Task-Manager / Power-Loss).
    /// Der Initial-Wert wird im Konstruktor direkt in das Backing-Field geschrieben — die
    /// partielle Methode feuert daher beim Initial-Load nicht, und es entsteht keine Save-Schleife.
    /// </summary>
    partial void OnIsTagCloudVisibleChanged(bool value) => PersistColumnLayout();

    /// <summary>Hot-Save fuer den Left-Tab-Index. Initial-Wert wird im Konstruktor
    /// direkt in das Backing-Field geschrieben — der Hook feuert beim Start nicht.</summary>
    partial void OnLeftTabIndexChanged(int value) => PersistColumnLayout();

    /// <inheritdoc />
    public async Task<bool> NavigateToWikiLinkAsync(string wikiLinkTarget, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wikiLinkTarget);
        Guid? targetId = await _documentLocator.FindByWikiLinkAsync(wikiLinkTarget, cancellationToken).ConfigureAwait(true);
        if (targetId is null)
        {
            LogWikiLinkUnresolved(_logger, wikiLinkTarget);
            return false;
        }
        return await NavigateToDocumentAsync(targetId.Value, cancellationToken).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task<bool> NavigateToDocumentAsync(Guid markdownFileId, CancellationToken cancellationToken)
    {
        if (markdownFileId == Guid.Empty)
        {
            return false;
        }
        await DocumentPanel.LoadAsync(markdownFileId, cancellationToken).ConfigureAwait(true);

        SearchResultItemViewModel? matchingItem = Search.Results.FirstOrDefault(item => item.MarkdownFileId == markdownFileId);
        if (matchingItem is not null)
        {
            Search.SelectedResult = matchingItem;
        }
        return true;
    }

    /// <summary>Trennt Event-Handler und gibt Ressourcen frei.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _messenger.UnregisterAll(this);
        FolderTree.PropertyChanged -= OnFolderTreeChanged;
        FolderTree.FileSelected -= OnFolderTreeFileSelected;
        AllFiles.FileSelected -= OnAllFilesFileSelected;
        Search.PropertyChanged -= OnSearchChanged;
        _healthProvider.Changed -= OnHealthChanged;
        _indexer.InitialScanProgress -= OnIndexerProgress;
        FolderTree.Dispose();
        Search.Dispose();
        TagCloud.Dispose();
        DocumentPanel.Dispose();
    }

    private void OnIndexerProgress(object? sender, IndexerScanProgressEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        int totalProcessed;
        lock (_perRootLock)
        {
            _perRootProcessed[args.Root] = args.ProcessedCount;
            totalProcessed = _perRootProcessed.Values.Sum();
        }
        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        _uiDispatcher.Invoke(() =>
        {
            IndexedFileCount = totalProcessed;
            LastIndexerRunUtc = nowUtc;
        });
    }

    private void OnHealthChanged(object? sender, EventArgs args)
    {
        _uiDispatcher.Invoke(() =>
        {
            Health = _healthProvider.Current;
            HealthDetail = _healthProvider.Detail;
        });
    }

    private void OnFolderTreeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (string.Equals(args.PropertyName, nameof(FolderTreeViewModel.SelectedPathPrefix), StringComparison.Ordinal))
        {
            Search.PathPrefixFilter = FolderTree.SelectedPathPrefix;
        }
    }

    private void OnSearchChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (string.Equals(args.PropertyName, nameof(SearchViewModel.SelectedResult), StringComparison.Ordinal))
        {
            SearchResultItemViewModel? selected = Search.SelectedResult;
            if (selected is null)
            {
                return;
            }
            _ = DocumentPanel.LoadAsync(selected.MarkdownFileId, CancellationToken.None);
        }
    }

    private void OnFolderTreeFileSelected(string absolutePath)
    {
        _ = NavigateToPathAsync(absolutePath);
    }

    private void OnAllFilesFileSelected(string absolutePath)
    {
        _ = NavigateToPathAsync(absolutePath);
    }

    private async Task NavigateToPathAsync(string absolutePath)
    {
        try
        {
            Guid? targetId = await _documentLocator.FindByAbsolutePathAsync(absolutePath, CancellationToken.None).ConfigureAwait(true);
            if (targetId is null)
            {
                // Indexer kennt die Datei noch nicht — Direct-Load aus dem Dateisystem,
                // damit der Nutzer Inhalt sieht, ohne auf den ersten Scan warten zu muessen.
                LogPathUnresolved(_logger, absolutePath);
                await DocumentPanel.LoadByPathAsync(absolutePath, CancellationToken.None).ConfigureAwait(true);
                return;
            }
            _ = await NavigateToDocumentAsync(targetId.Value, CancellationToken.None).ConfigureAwait(true);
        }
        catch (DbException exception)
        {
            // SQLite-Spitze beim Locator-Lookup — Navigation still verwerfen.
            LogNavigationFailed(_logger, absolutePath, exception);
        }
    }

    [LoggerMessage(EventId = 200, Level = LogLevel.Information, Message = "MainViewModel erstellt.")]
    private static partial void LogCreated(ILogger logger);

    [LoggerMessage(EventId = 320, Level = LogLevel.Information, Message = "WikiLink-Ziel {WikiLinkTarget} konnte nicht aufgelöst werden.")]
    private static partial void LogWikiLinkUnresolved(ILogger logger, string wikiLinkTarget);

    [LoggerMessage(EventId = 321, Level = LogLevel.Information, Message = "Datei {AbsolutePath} ist noch nicht indiziert — Klick im Tree ignoriert.")]
    private static partial void LogPathUnresolved(ILogger logger, string absolutePath);

    [LoggerMessage(EventId = 322, Level = LogLevel.Warning, Message = "Navigation zu {AbsolutePath} fehlgeschlagen — Datenbank-Spitze.")]
    private static partial void LogNavigationFailed(ILogger logger, string absolutePath, Exception exception);
}
