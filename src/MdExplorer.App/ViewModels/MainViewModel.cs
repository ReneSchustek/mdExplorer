using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Services;
using MdExplorer.Core.Abstractions;
using MdExplorer.TagCloud.ViewModels;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Orchestriert die Bereiche des Hauptfensters: Ordnerbaum, Alle Dateien, Suche, Dokument und
/// Tag-Cloud. Leitet Klicks auf verwandte Dokumente, Ordner und Kennzeichnungen weiter und
/// liefert die gespeicherten Spaltenbreiten. Statuszeile und Update-Hinweis sind eigene Teile.
/// </summary>
internal sealed partial class MainViewModel : ObservableObject, INavigationService, IDisposable
{
    /// <summary>Stelle des Registers „Suche" in der linken Spalte.</summary>
    private const int SearchTabIndex = 2;

    /// <summary>Der Produktname, wie er in der Titelzeile steht; er wird nicht übersetzt.</summary>
    private const string ProductName = "MdExplorer";

    private readonly MainServices _services;
    private readonly ILogger<MainViewModel> _logger;
    private readonly PanelSelectionCoordinator _selection;
    private readonly TagCloudVisibility _tagCloudVisibility = new();

    /// <summary>
    /// Der Merker, der beim Beenden der Anwendung anschlägt.
    /// </summary>
    /// <remarks>
    /// Er kommt aus dem Wirt (<c>IHostApplicationLifetime.ApplicationStopping</c>). Ohne ihn
    /// wartete das Schließen auf Arbeit, die niemand mehr braucht. Ohne Wirt, also im Test,
    /// bleibt er der leere Merker.
    /// </remarks>
    private readonly CancellationToken _shutdownToken;
    private bool _disposed;

    [ObservableProperty]
    private double _folderColumnWidth;

    [ObservableProperty]
    private double _previewColumnWidth;

    [ObservableProperty]
    private bool _isTagCloudVisible;

    [ObservableProperty]
    private int _leftTabIndex;

    /// <summary>Erzeugt das ViewModel und verbindet die Bereiche.</summary>
    /// <param name="panels">Die Bereiche des Hauptfensters.</param>
    /// <param name="services">Die Dienste, auf die zugegriffen wird.</param>
    /// <param name="logger">Protokoll.</param>
    /// <param name="shutdownToken">Schlägt beim Beenden der Anwendung an.</param>
    public MainViewModel(MainPanels panels, MainServices services, ILogger<MainViewModel> logger, CancellationToken shutdownToken = default)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        FolderTree = panels.FolderTree;
        AllFiles = panels.AllFiles;
        Search = panels.Search;
        DocumentPanel = panels.DocumentPanel;
        TagCloud = panels.TagCloud;
        _services = services;
        _logger = logger;
        _shutdownToken = shutdownToken;
        StorageLocation = services.SettingsStore.StorageLocation;
        StatusBar = new StatusBarViewModel(services);
        UpdateBanner = new UpdateBannerViewModel(services.UiDispatcher, services.Messenger);

        // Direkt in die Felder: Über die Eigenschaften sprängen die Speicher-Haken an, und das
        // Laden schriebe dieselbe Datei gleich wieder.
        UiLayout layout = services.SettingsStore.Load();
        _folderColumnWidth = layout.FolderColumnWidth;
        _previewColumnWidth = layout.PreviewColumnWidth;
        _isTagCloudVisible = layout.IsTagCloudVisible;
        _leftTabIndex = layout.LeftTabIndex;

        _selection = new PanelSelectionCoordinator(
            panels,
            services.UiDispatcher,
            id => DocumentPanel.LoadAsync(id, _shutdownToken),
            NavigateToPathAsync,
            logger);
        Subscribe();
        LogCreated(logger);
    }

    /// <summary>Linke Spalte, Register Ordnerbaum.</summary>
    public FolderTreeViewModel FolderTree { get; }

    /// <summary>Linke Spalte, Register mit der flachen Liste aller indizierten Dateien.</summary>
    public AllFilesViewModel AllFiles { get; }

    /// <summary>Linke Spalte, Register Suche.</summary>
    public SearchViewModel Search { get; }

    /// <summary>Mittlere Spalte (Lesen und Bearbeiten).</summary>
    public DocumentPanelViewModel DocumentPanel { get; }

    /// <summary>Tag-Cloud-Panel (zuschaltbar).</summary>
    public TagCloudViewModel TagCloud { get; }

    /// <summary>Statuszeile: Indexstand und Betriebsanzeige.</summary>
    public StatusBarViewModel StatusBar { get; }

    /// <summary>Hinweis auf eine neuere Fassung.</summary>
    public UpdateBannerViewModel UpdateBanner { get; }

    /// <summary>Titelzeile des Hauptfensters.</summary>
    public string Title { get; } = ProductName;

    /// <summary>Pfad-Anzeige in der Statusleiste: Speicherort der Settings/Datenbank.</summary>
    public string StorageLocation { get; }

    /// <summary>Wahr, solange der Editor Änderungen hält, die nicht auf der Platte stehen.</summary>
    public bool HasUnsavedChanges => DocumentPanel.HasUnsavedChanges;

    /// <summary>Merkt sich die Spaltenbreiten beim Schließen für den nächsten Start.</summary>
    /// <param name="folderWidth">Breite der linken Spalte.</param>
    /// <param name="previewWidth">Breite der Vorschau.</param>
    public void RememberColumnWidths(double folderWidth, double previewWidth)
    {
        FolderColumnWidth = folderWidth;
        PreviewColumnWidth = previewWidth;
        PersistColumnLayout();
    }

    /// <summary>Persistiert die aktuellen Spaltenbreiten, Tag-Cloud-Sichtbarkeit und Left-Tab-Index.</summary>
    public void PersistColumnLayout()
    {
        // Hilfe- und Graph-Fenster schreiben ihre Felder in dieselbe Datei. Deshalb wird der
        // gespeicherte Stand fortgeschrieben, statt ein neues Layout ohne ihre Felder anzulegen.
        UiSettingsStore store = _services.SettingsStore;
        store.Save(store.Load() with
        {
            FolderColumnWidth = FolderColumnWidth,
            PreviewColumnWidth = PreviewColumnWidth,
            IsTagCloudVisible = IsTagCloudVisible,
            LeftTabIndex = LeftTabIndex,
        });
    }

    /// <summary>
    /// Öffnet eine Datei, die von außen kommt (Doppelklick, „Öffnen mit“), auch wenn sie in keinem
    /// indizierten Ordner liegt.
    /// </summary>
    /// <param name="absolutePath">Der geprüfte, vollständige Pfad.</param>
    /// <returns><see langword="false"/>, wenn der Nutzer bei ungespeicherten Änderungen abgebrochen hat.</returns>
    public Task<bool> OpenFileAsync(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        return NavigateToPathAsync(absolutePath);
    }

    /// <summary>Wechselt in der linken Spalte auf das Register „Suche".</summary>
    public void ShowSearchTab() => LeftTabIndex = SearchTabIndex;

    /// <summary>Fragt vor dem Schließen des Hauptfensters nach ungespeicherten Änderungen.</summary>
    /// <returns><see langword="true"/>, wenn das Fenster schließen darf.</returns>
    public Task<bool> ConfirmCloseAsync() => DocumentPanel.ConfirmLeaveAsync(CancellationToken.None);

    /// <summary>
    /// Ob die Tag-Cloud gerade zu sehen ist: die gespeicherte Wahl, in einem schmalen Fenster
    /// eingeschränkt durch <see cref="TagCloudVisibility"/>.
    /// </summary>
    /// <remarks>
    /// Setzbar, damit ein Schalter in beide Richtungen binden kann. Ein Schalter, der nur einen
    /// Befehl auslöst, wechselt über die Bedienungshilfen nur seinen Haken, nicht den Zustand.
    /// </remarks>
    public bool IsTagCloudShown
    {
        get => _tagCloudVisibility.IsShown(IsTagCloudVisible);
        set
        {
            if (value != IsTagCloudShown)
            {
                ToggleTagCloud();
            }
        }
    }

    /// <summary>Schaltet die Tag-Cloud um, wie der Nutzer sie sieht (Knopf, Strg+T). Gespeichert wird im <see cref="OnIsTagCloudVisibleChanged(bool)"/>-Hook.</summary>
    [RelayCommand]
    public void ToggleTagCloud()
    {
        bool preferred = _tagCloudVisibility.Toggle(IsTagCloudVisible);
        if (preferred == IsTagCloudVisible)
        {
            // Nur im schmalen Fenster: Die Wahl bleibt, aber die Tag-Cloud erscheint.
            OnPropertyChanged(nameof(IsTagCloudShown));
            return;
        }
        IsTagCloudVisible = preferred;
    }

    /// <summary>Blendet die Tag-Cloud aus (✕ in ihrem Kopf).</summary>
    [RelayCommand]
    public void HideTagCloud() => IsTagCloudShown = false;

    /// <summary>Nimmt die Breite des Hauptfensters auf; ein schmales Fenster blendet die Tag-Cloud aus.</summary>
    /// <param name="width">Die Breite in geräteunabhängigen Einheiten.</param>
    public void UpdateWindowWidth(double width)
    {
        if (_tagCloudVisibility.UpdateWidth(width))
        {
            OnPropertyChanged(nameof(IsTagCloudShown));
        }
    }

    /// <inheritdoc />
    public async Task<bool> NavigateToWikiLinkAsync(string wikiLinkTarget, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wikiLinkTarget);
        Guid? targetId = await _services.DocumentLocator.FindByWikiLinkAsync(wikiLinkTarget, cancellationToken).ConfigureAwait(true);
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
        if (!await DocumentPanel.LoadAsync(markdownFileId, cancellationToken).ConfigureAwait(true))
        {
            return false;
        }

        _selection.MarkOpenSearchResult(markdownFileId);
        return true;
    }

    /// <summary>Trennt die Abonnements. Die Bereiche gibt die Abhängigkeitsverwaltung frei, die sie erzeugt hat.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _selection.Dispose();
        StatusBar.Dispose();
        UpdateBanner.Dispose();
        DocumentPanel.RelatedDocumentRequested -= OnRelatedDocumentRequested;
        DocumentPanel.FolderRequested -= OnFolderRequested;
        DocumentPanel.TagRequested -= OnTagRequested;
        DocumentPanel.DocumentChanged -= OnDocumentChanged;
    }

    [LoggerMessage(EventId = 200, Level = LogLevel.Information, Message = "MainViewModel erstellt.")]
    private static partial void LogCreated(ILogger logger);

    [LoggerMessage(EventId = 320, Level = LogLevel.Information, Message = "WikiLink-Ziel {WikiLinkTarget} konnte nicht aufgelöst werden.")]
    private static partial void LogWikiLinkUnresolved(ILogger logger, string wikiLinkTarget);

    /// <summary>
    /// Speichert sofort. Jeder Wechsel der Sichtbarkeit (Befehl, Menü-Haken, Strg+T) wird
    /// gleich persistiert, damit der Stand beim nächsten Start auch dann erhalten
    /// bleibt, wenn das Hauptfenster ungewöhnlich beendet wird (Task-Manager / Power-Loss).
    /// Der Initial-Wert wird im Konstruktor direkt in das Backing-Field geschrieben; die
    /// partielle Methode feuert daher beim Initial-Load nicht, und es entsteht keine Save-Schleife.
    /// </summary>
    partial void OnIsTagCloudVisibleChanged(bool value)
    {
        PersistColumnLayout();
        OnPropertyChanged(nameof(IsTagCloudShown));
    }

    /// <summary>Hot-Save für den Left-Tab-Index. Initial-Wert wird im Konstruktor
    /// direkt in das Backing-Field geschrieben. Der Hook feuert beim Start nicht.</summary>
    partial void OnLeftTabIndexChanged(int value) => PersistColumnLayout();

    private void Subscribe()
    {
        DocumentPanel.RelatedDocumentRequested += OnRelatedDocumentRequested;
        DocumentPanel.FolderRequested += OnFolderRequested;
        DocumentPanel.TagRequested += OnTagRequested;
        DocumentPanel.DocumentChanged += OnDocumentChanged;
    }

    /// <summary>Öffnet ein Dokument, das am geöffneten hängt.</summary>
    private void OnRelatedDocumentRequested(Guid markdownFileId) =>
        ObservedTask.Forget(NavigateToDocumentAsync(markdownFileId, _shutdownToken), _logger);

    /// <summary>
    /// Zeigt, was sonst noch im selben Ordner liegt.
    /// </summary>
    /// <remarks>
    /// Über die vorhandene Suche mit ihrem Pfad-Filter statt über einen zweiten Weg: Der
    /// Nutzer sieht damit dieselbe Trefferliste wie bei einer getippten Anfrage, und es gibt
    /// nur eine Stelle, an der eine Einschränkung wirkt.
    /// </remarks>
    private void OnFolderRequested(string folderPath) => ShowInSearch(SearchQueryText.PathFilter(folderPath));

    /// <summary>Zeigt, was dieselbe Kennzeichnung trägt.</summary>
    private void OnTagRequested(string tagSlug) => ShowInSearch($"tag:{tagSlug}");

    /// <summary>
    /// Zieht die Ansicht nach, wenn ein Vorgang die Datei verändert hat.
    /// </summary>
    /// <remarks>
    /// Nach dem Umbenennen oder Verschieben zeigt die Ansicht dieselbe Datei unter ihrem
    /// neuen Pfad. Nach dem Löschen wird der ganze Bereich geleert: Bliebe der Editor auf der
    /// gelöschten Datei, legte ein Speichern sie neu an. Die Liste wird neu geladen, weil ihr
    /// Eintrag sonst auf einen Pfad zeigt, den es nicht mehr gibt.
    /// </remarks>
    private void OnDocumentChanged(string? newAbsolutePath)
    {
        ObservedTask.Forget(AllFiles.RefreshAsync(), _logger);

        if (newAbsolutePath is null)
        {
            DocumentPanel.Clear();
            return;
        }

        ObservedTask.Forget(NavigateToPathAsync(newAbsolutePath), _logger);
    }

    private void ShowInSearch(string query)
    {
        Search.QueryText = query;
        ShowSearchTab();
    }

    /// <summary>
    /// Öffnet eine Datei über ihren Pfad. Ob sie schon im Index steht, klärt der Dokumentbereich
    /// mit einer einzigen Abfrage.
    /// </summary>
    /// <returns><see langword="false"/>, wenn der Nutzer bei ungespeicherten Änderungen abgebrochen hat.</returns>
    private async Task<bool> NavigateToPathAsync(string absolutePath)
    {
        if (!await DocumentPanel.LoadByPathAsync(absolutePath, _shutdownToken).ConfigureAwait(true))
        {
            return false;
        }

        _selection.MarkOpenSearchResult(DocumentPanel.OpenDocumentId);
        return true;
    }
}
