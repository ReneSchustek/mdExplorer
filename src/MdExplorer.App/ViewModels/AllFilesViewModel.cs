using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Localization;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// ViewModel des Registers „Alle Dateien". Lädt die flache Liste indizierter Markdown-Dateien
/// samt Tag-Slugs einmal über <see cref="IAllFilesQuery"/>; Suche (Teilzeichenkette in Titel,
/// Pfad und Tags), Ordner-, Tag- und Zeitraumfilter (<see cref="AllFilesFilterSet"/>) sowie die
/// Sortierung laufen danach im Speicher, ohne neue Abfrage.
/// </summary>
internal sealed partial class AllFilesViewModel : ObservableObject
{
    /// <summary>Ab dieser Zahl sichtbarer Einträge erscheint die Sprungleiste.</summary>
    /// <remarks>Darunter überblickt man die Liste ohne Sprungziel; die Leiste wäre nur Fläche.</remarks>
    private const int JumpBarThreshold = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AllFilesViewModel> _logger;

    /// <summary>
    /// Der Merker, der beim Beenden der Anwendung anschlägt.
    /// </summary>
    /// <remarks>
    /// Er kommt aus dem Wirt (<c>IHostApplicationLifetime.ApplicationStopping</c>). Ohne ihn
    /// wartete das Schließen auf eine Abfrage, die niemand mehr braucht. Ohne Wirt, also im
    /// Test, bleibt er der leere Merker.
    /// </remarks>
    private readonly CancellationToken _shutdownToken;
    private readonly TimeProvider _timeProvider;
    private readonly AllFilesFilterSet _filters;

    private AllFilesItemViewModel[] _allItems = [];
    private bool _refreshRequested;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private AllFilesSortMode _sortMode = AllFilesSortMode.LastModified;

    /// <remarks>
    /// Schützt vor dem zweiten Lauf, während der erste noch läuft: ein Ablaufmerker, kein
    /// Anzeigezustand. Was die Ansicht zeigt, steht ausschließlich in <see cref="State"/>.
    /// </remarks>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Der Zustand, auf den die Ansicht reagiert, die einzige Quelle dafür.</summary>
    [ObservableProperty]
    private AllFilesListState _state = AllFilesListState.Loading;

    [ObservableProperty]
    private AllFilesItemViewModel? _selectedItem;

    /// <summary>Erzeugt das ViewModel und verdrahtet die Befehle.</summary>
    public AllFilesViewModel(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<AllFilesViewModel> logger,
        CancellationToken shutdownToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
        _shutdownToken = shutdownToken;
        _filters = new AllFilesFilterSet(timeProvider);
        Items = [];
        ActiveFilters = [];
        PeriodFilters =
        [
            new PeriodFilterViewModel(AllFilesPeriod.Any, UiText.Get("AllFiles_Period_Any")) { IsActive = true },
            new PeriodFilterViewModel(AllFilesPeriod.Today, UiText.Get("AllFiles_Period_Today")),
            new PeriodFilterViewModel(AllFilesPeriod.LastSevenDays, UiText.Get("AllFiles_Period_SevenDays")),
            new PeriodFilterViewModel(AllFilesPeriod.LastThirtyDays, UiText.Get("AllFiles_Period_ThirtyDays")),
        ];
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        JumpToLetterCommand = new RelayCommand<string>(RaiseJumpRequested);
        FilterByFolderCommand = new RelayCommand<string>(FilterByFolder);
        FilterByTagCommand = new RelayCommand<string>(tag => ApplyIf(!string.IsNullOrWhiteSpace(tag) && _filters.AddTag(tag)));
        SelectPeriodCommand = new RelayCommand<AllFilesPeriod>(period => ApplyIf(_filters.SetPeriod(period)));
        RemoveFilterCommand = new RelayCommand<ActiveFilterViewModel>(filter => ApplyIf(filter is not null && _filters.Remove(filter)));
        ResetSearchAndFiltersCommand = new RelayCommand(ResetSearchAndFilters);
    }

    /// <summary>Wird ausgelöst, sobald ein Eintrag ausgewählt wird (mit absolutem Pfad).</summary>
    public event Action<string>? FileSelected;

    /// <summary>Wird ausgelöst, wenn zu einer Buchstabengruppe gesprungen werden soll.</summary>
    public event Action<char>? JumpRequested;

    /// <summary>Aktuelle (gefilterte + sortierte) Sicht auf die Datei-Liste.</summary>
    public ObservableCollection<AllFilesItemViewModel> Items { get; }

    /// <summary>Löst einen Lade-Roundtrip aus.</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Die Buchstaben der Sprungleiste, immer vollständig, nie gekürzt.</summary>
    public IReadOnlyList<JumpLetterViewModel> JumpLetters { get; } =
        [.. AlphabetIndex.Letters.Select(letter => new JumpLetterViewModel(letter))];

    /// <summary>Leert das Suchfeld.</summary>
    public RelayCommand ClearSearchCommand { get; }

    /// <summary>Springt zur Gruppe eines Buchstabens.</summary>
    public RelayCommand<string> JumpToLetterCommand { get; }

    /// <summary>Die wählbaren Änderungszeiträume: feste Menge, deshalb als Umschalter.</summary>
    public IReadOnlyList<PeriodFilterViewModel> PeriodFilters { get; }

    /// <summary>
    /// Die gerade wirkenden Filter, jeder einzeln entfernbar.
    /// </summary>
    /// <remarks>
    /// Ein Filter, der wirkt, aber nicht zu sehen ist, erzeugt den Eindruck fehlender
    /// Daten, und man sucht den Fehler dann in den Daten statt in der Einschränkung.
    /// </remarks>
    public ObservableCollection<ActiveFilterViewModel> ActiveFilters { get; }

    /// <summary>Schränkt auf den Ordner eines Eintrags ein.</summary>
    public RelayCommand<string> FilterByFolderCommand { get; }

    /// <summary>Schränkt auf eine Kennzeichnung ein.</summary>
    public RelayCommand<string> FilterByTagCommand { get; }

    /// <summary>Wählt den Änderungszeitraum.</summary>
    public RelayCommand<AllFilesPeriod> SelectPeriodCommand { get; }

    /// <summary>Nimmt einen einzelnen Filter zurück.</summary>
    public RelayCommand<ActiveFilterViewModel> RemoveFilterCommand { get; }

    /// <summary>Setzt Suche und alle Filter zurück.</summary>
    public RelayCommand ResetSearchAndFiltersCommand { get; }

    /// <summary>
    /// Ob die Sprungleiste angezeigt wird.
    /// </summary>
    /// <remarks>
    /// Nur bei alphabetischer Sortierung: Nach Datum sortiert stünde ein Sprung auf „M"
    /// an einer beliebigen Stelle der Liste. Und erst ab einer Bestandsgröße, ab der man
    /// die Liste nicht mehr überblickt; darunter ist die Leiste nur zusätzliche Fläche.
    /// </remarks>
    public bool IsJumpBarVisible => AllFilesOrdering.IsAlphabetical(SortMode) && Items.Count >= JumpBarThreshold;

    /// <summary>Es gibt überhaupt keine Einträge, unabhängig von Suche und Filter.</summary>
    public bool ShowsNothingAtAll => State == AllFilesListState.EmptyStock;

    /// <summary>Es gibt Einträge, aber keiner passt zu Suche und Filtern.</summary>
    /// <remarks>
    /// Zwei verschiedene Lagen, die zwei verschiedene Sätze brauchen: „noch nichts
    /// indiziert" ist ein Zustand des Bestands, „nichts gefunden" einer der Suche. Wer
    /// beides gleich beschriftet, schickt den Nutzer in die falsche Richtung.
    /// </remarks>
    public bool ShowsNoMatches => State == AllFilesListState.NoMatches;

    /// <summary>Das Laden ist fehlgeschlagen.</summary>
    /// <remarks>
    /// Die dritte Lage, die sonst wie die erste aussieht: Eine leere Liste nach einem
    /// Fehler behauptet „nichts vorhanden" und schickt den Nutzer in den Index statt ins
    /// Protokoll. Ein Fehler gehört sichtbar, nicht nur ins Log.
    /// </remarks>
    public bool ShowsLoadFailure => State == AllFilesListState.LoadFailed;

    /// <summary>Lädt die flache Datei-Liste aus dem Indexer-Store.</summary>
    /// <remarks>
    /// Ein Aufruf während eines laufenden Ladens wird vorgemerkt und danach ausgeführt. Er kommt
    /// etwa nach dem Umbenennen einer Datei; liefe er ins Leere, zeigte die Liste den alten Pfad.
    /// </remarks>
    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            _refreshRequested = true;
            return;
        }
        IsBusy = true;
        try
        {
            do
            {
                _refreshRequested = false;
                await LoadOnceAsync().ConfigureAwait(true);
            }
            while (_refreshRequested);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [LoggerMessage(EventId = 1300, Level = LogLevel.Information, Message = "Alle-Dateien-Tab geladen — {Count} Einträge.")]
    private static partial void LogLoaded(ILogger logger, int count);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Warning, Message = "Alle-Dateien-Tab konnte nicht geladen werden.")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);

    private async Task LoadOnceAsync()
    {
        State = AllFilesListState.Loading;
        try
        {
            AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(true))
            {
                IAllFilesQuery query = scope.ServiceProvider.GetRequiredService<IAllFilesQuery>();
                IReadOnlyList<AllFilesRow> rows = await query.GetAllAsync(_shutdownToken).ConfigureAwait(true);
                TimeZoneInfo timeZone = _timeProvider.LocalTimeZone;
                _allItems = [.. rows.Select(row => new AllFilesItemViewModel(row, timeZone))];
                ApplyViewState();
                LogLoaded(_logger, _allItems.Length);
            }
        }
        catch (OperationCanceledException)
        {
            // Die Anwendung wird beendet; die Liste sieht niemand mehr.
        }
        catch (Exception exception) when (DataLoadFailure.IsExpected(exception))
        {
            // Der Fehler endet nicht im Log: Die Ansicht sagt es, sonst hält der Nutzer
            // seinen Bestand für leer und sucht am falschen Ende.
            LogRefreshFailed(_logger, exception);
            State = AllFilesListState.LoadFailed;
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyViewState();

    partial void OnSortModeChanged(AllFilesSortMode value) => ApplyViewState();

    partial void OnSelectedItemChanged(AllFilesItemViewModel? value)
    {
        if (value is not null)
        {
            FileSelected?.Invoke(value.AbsolutePath);
        }
    }

    partial void OnStateChanged(AllFilesListState value)
    {
        OnPropertyChanged(nameof(ShowsNothingAtAll));
        OnPropertyChanged(nameof(ShowsNoMatches));
        OnPropertyChanged(nameof(ShowsLoadFailure));
    }

    /// <summary>Baut die Sicht neu auf, wenn ein Befehl tatsächlich etwas geändert hat.</summary>
    private void ApplyIf(bool changed)
    {
        if (changed)
        {
            ApplyViewState();
        }
    }

    private void FilterByFolder(string? folderPath)
    {
        if (folderPath is null)
        {
            return;
        }
        _filters.SetFolder(folderPath);
        ApplyViewState();
    }

    private void ApplyViewState()
    {
        RebuildItems();
        UpdateJumpLetters();
        UpdateActiveFilters();

        // Ein Zustand, aus dem sich alles ableitet, statt mehrerer Merker, die
        // auseinanderlaufen können.
        State = (_allItems.Length, Items.Count) switch
        {
            (0, _) => AllFilesListState.EmptyStock,
            (_, 0) => AllFilesListState.NoMatches,
            _ => AllFilesListState.Items,
        };

        OnPropertyChanged(nameof(IsJumpBarVisible));
    }

    /// <summary>Füllt die sichtbare Liste mit den Einträgen, die Suche und Filter passieren, und beschriftet die Gruppen.</summary>
    private void RebuildItems()
    {
        IEnumerable<AllFilesItemViewModel> matches = _allItems.Where(_filters.CreateMatcher(SearchText ?? string.Empty));
        Items.Clear();
        foreach (AllFilesItemViewModel item in AllFilesOrdering.Sort(matches, SortMode))
        {
            item.GroupLabel = AllFilesOrdering.GroupLabelOf(item, SortMode);
            Items.Add(item);
        }
    }

    /// <summary>Stellt die Chip-Leiste der wirkenden Filter neu zusammen.</summary>
    private void UpdateActiveFilters()
    {
        ActiveFilters.Clear();
        foreach (ActiveFilterViewModel chip in _filters.Chips(PeriodFilters))
        {
            ActiveFilters.Add(chip);
        }

        foreach (PeriodFilterViewModel filter in PeriodFilters)
        {
            filter.IsActive = filter.Period == _filters.Period;
        }
    }

    private void ResetSearchAndFilters()
    {
        _filters.Reset();

        // Setzt die Ansicht selbst dann neu zusammen, wenn der Suchtext schon leer war,
        // sonst bliebe das Zurücknehmen der Filter ohne Wirkung.
        if (SearchText.Length == 0)
        {
            ApplyViewState();
            return;
        }

        SearchText = string.Empty;
    }

    /// <summary>
    /// Markiert, welche Buchstaben Einträge haben.
    /// </summary>
    /// <remarks>
    /// Buchstaben ohne Einträge werden deaktiviert, nicht entfernt: Eine Leiste, die ihre
    /// Breite je nach Bestand ändert, ist kein verlässlicher Anlaufpunkt.
    /// </remarks>
    private void UpdateJumpLetters()
    {
        HashSet<char> occupied = AllFilesOrdering.OccupiedLetters(Items, SortMode);

        foreach (JumpLetterViewModel letter in JumpLetters)
        {
            letter.HasEntries = occupied.Contains(letter.Letter);
        }
    }

    private void RaiseJumpRequested(string? letter)
    {
        if (!string.IsNullOrEmpty(letter))
        {
            JumpRequested?.Invoke(letter[0]);
        }
    }
}

/// <summary>Ein Buchstabe der Sprungleiste.</summary>
internal sealed partial class JumpLetterViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _hasEntries;

    /// <summary>Erzeugt den Eintrag für einen Buchstaben.</summary>
    public JumpLetterViewModel(char letter)
    {
        Letter = letter;
        Label = letter.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Der Buchstabe selbst.</summary>
    public char Letter { get; }

    /// <summary>Der Buchstabe als Text, für Anzeige und Befehlsparameter.</summary>
    public string Label { get; }
}

/// <summary>Was die Datei-Liste gerade zeigt.</summary>
/// <remarks>
/// Ein Zustand statt mehrerer bools nebeneinander: Zwei Merker, die sich widersprechen,
/// ergeben eine Ansicht, die es laut Code nicht geben kann, und genau die sieht dann der
/// Nutzer. Die Anzeige-Eigenschaften leiten sich hieraus ab.
/// </remarks>
internal enum AllFilesListState
{
    /// <summary>Der Bestand wird gerade geholt.</summary>
    Loading = 0,

    /// <summary>Es ist überhaupt nichts indiziert.</summary>
    EmptyStock = 1,

    /// <summary>Es gibt Einträge, aber keiner passt zu Suche und Filtern.</summary>
    NoMatches = 2,

    /// <summary>Es gibt etwas zu zeigen.</summary>
    Items = 3,

    /// <summary>Der Bestand konnte nicht geholt werden.</summary>
    LoadFailed = 4,
}

/// <summary>Ein wählbarer Änderungszeitraum.</summary>
internal sealed partial class PeriodFilterViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    /// <summary>Erzeugt den Umschalter für einen Zeitraum.</summary>
    public PeriodFilterViewModel(AllFilesPeriod period, string label)
    {
        Period = period;
        Label = label;
    }

    /// <summary>Der Zeitraum, für den dieser Umschalter steht.</summary>
    public AllFilesPeriod Period { get; }

    /// <summary>Die Aufschrift.</summary>
    public string Label { get; }
}

/// <summary>Ein Filter, der gerade wirkt, als entfernbarer Chip dargestellt.</summary>
internal sealed class ActiveFilterViewModel
{
    /// <summary>Erzeugt den Chip zu einem wirkenden Filter.</summary>
    public ActiveFilterViewModel(AllFilesFilterKind kind, string value, string label)
    {
        Kind = kind;
        Value = value;
        Label = label;
    }

    /// <summary>Woran der Filter greift.</summary>
    public AllFilesFilterKind Kind { get; }

    /// <summary>Der eingestellte Wert: Ordner, Kennzeichnung oder Zeitraum.</summary>
    public string Value { get; }

    /// <summary>Die Aufschrift des Chips.</summary>
    public string Label { get; }

    /// <summary>Beschriftung der Entfernen-Schaltfläche, auch für die Sprachausgabe.</summary>
    public string RemoveHint => UiText.Format("AllFiles_Filter_Remove", Label);
}

/// <summary>Woran ein Filter der Datei-Liste greift.</summary>
internal enum AllFilesFilterKind
{
    /// <summary>Der Ordner, in dem eine Datei liegt.</summary>
    Folder = 0,

    /// <summary>Eine Kennzeichnung der Datei.</summary>
    Tag = 1,

    /// <summary>Der Zeitraum der letzten Änderung.</summary>
    Period = 2,
}

/// <summary>Zeiträume, auf die sich die Liste einschränken lässt.</summary>
internal enum AllFilesPeriod
{
    /// <summary>Ohne Einschränkung.</summary>
    Any = 0,

    /// <summary>Seit Mitternacht der Ortszeit geändert.</summary>
    Today = 1,

    /// <summary>In den letzten sieben Tagen geändert.</summary>
    LastSevenDays = 2,

    /// <summary>In den letzten dreißig Tagen geändert.</summary>
    LastThirtyDays = 3,
}

/// <summary>Sortier-Modi für die Datei-Liste.</summary>
internal enum AllFilesSortMode
{
    /// <summary>Standard: nach Schreibdatum absteigend.</summary>
    LastModified = 0,

    /// <summary>Nach Dateiname (Titel) aufsteigend.</summary>
    Title = 1,

    /// <summary>Nach relativem Pfad aufsteigend.</summary>
    RelativePath = 2,
}

/// <summary>Item-View für einen Datei-Eintrag im Alle-Dateien-Tab.</summary>
internal sealed class AllFilesItemViewModel
{
    /// <summary>Erzeugt einen Eintrag aus einer Query-Zeile.</summary>
    public AllFilesItemViewModel(AllFilesRow row, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(timeZone);
        MarkdownFileId = row.MarkdownFileId;
        Title = row.Title;
        RelativePath = row.RelativePath;
        AbsolutePath = row.AbsolutePath;
        LastWriteTimeUtc = row.LastWriteTimeUtc;
        LastWriteTime = TimeZoneInfo.ConvertTimeFromUtc(row.LastWriteTimeUtc, timeZone);
        TagSlugs = row.TagSlugs;
        FolderPath = RelativeFolder.Of(row.RelativePath);
    }

    /// <summary>Stabiler Schlüssel.</summary>
    public Guid MarkdownFileId { get; }

    /// <summary>Dateiname ohne Erweiterung.</summary>
    public string Title { get; }

    /// <summary>Pfad relativ zum konfigurierten Root.</summary>
    public string RelativePath { get; }

    /// <summary>Vollqualifizierter Pfad, Eingabe für den Navigations-Locator.</summary>
    public string AbsolutePath { get; }

    /// <summary>Letzte Änderung auf Disk (UTC), Grundlage für Sortierung und Zeitraumfilter.</summary>
    public DateTime LastWriteTimeUtc { get; }

    /// <summary>
    /// Dieselbe Änderung in der Zeitzone des Rechners. Nur diese wird angezeigt: Wer eine
    /// Datei gerade bearbeitet hat, erwartet die Uhrzeit seiner eigenen Uhr.
    /// </summary>
    public DateTime LastWriteTime { get; }

    /// <summary>Slugs der angewendeten Tags.</summary>
    public IReadOnlyList<string> TagSlugs { get; }

    /// <summary>
    /// Beschriftung der Gruppe, in der dieser Eintrag steht; leer, wenn nicht gruppiert wird.
    /// </summary>
    public string GroupLabel { get; internal set; } = string.Empty;

    /// <summary>Der Ordner relativ zur Wurzel; leer für Dateien, die direkt darin liegen.</summary>
    public string FolderPath { get; }
}
