using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// ViewModel des "Alle Dateien"-Tabs. Lädt die flache Liste indizierter
/// Markdown-Dateien inklusive ihrer Tag-Slugs über <see cref="IAllFilesQuery"/> und
/// filtert sie clientseitig nach Substring-Match in Titel, Pfad und Tags. Sortierung
/// und Tag-Klick (Filter-Token) sind UI-getrieben.
/// </summary>
internal sealed partial class AllFilesViewModel : ObservableObject
{
    /// <summary>Ab dieser Zahl sichtbarer Einträge erscheint die Sprungleiste.</summary>
    /// <remarks>Darunter überblickt man die Liste ohne Sprungziel — die Leiste wäre nur Fläche.</remarks>
    private const int JumpBarThreshold = 50;

    /// <summary>Tage, die der Zeitraum „7 Tage" umfasst — einschließlich des laufenden.</summary>
    private const int SevenDayWindow = 7;

    /// <summary>Tage, die der Zeitraum „30 Tage" umfasst — einschließlich des laufenden.</summary>
    private const int ThirtyDayWindow = 30;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AllFilesViewModel> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly HashSet<string> _tagFilters = new(StringComparer.OrdinalIgnoreCase);

    private AllFilesItemViewModel[] _allItems = [];
    private string? _folderFilter;
    private AllFilesPeriod _period = AllFilesPeriod.Any;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private AllFilesSortMode _sortMode = AllFilesSortMode.LastModified;

    /// <remarks>
    /// Schützt vor dem zweiten Lauf, während der erste noch läuft — ein Ablaufmerker, kein
    /// Anzeigezustand. Was die Ansicht zeigt, steht ausschließlich in <see cref="State"/>.
    /// </remarks>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Der Zustand, auf den die Ansicht reagiert — die einzige Quelle dafür.</summary>
    [ObservableProperty]
    private AllFilesListState _state = AllFilesListState.Loading;

    [ObservableProperty]
    private AllFilesItemViewModel? _selectedItem;

    /// <summary>Wird ausgelöst, sobald ein Eintrag ausgewählt wird (mit absolutem Pfad).</summary>
    public event Action<string>? FileSelected;

    /// <summary>Erzeugt das ViewModel und verdrahtet die Refresh-Aktion.</summary>
    public AllFilesViewModel(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<AllFilesViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
        Items = [];
        ActiveFilters = [];
        PeriodFilters =
        [
            new PeriodFilterViewModel(AllFilesPeriod.Any, "Alle") { IsActive = true },
            new PeriodFilterViewModel(AllFilesPeriod.Today, "Heute"),
            new PeriodFilterViewModel(AllFilesPeriod.LastSevenDays, "7 Tage"),
            new PeriodFilterViewModel(AllFilesPeriod.LastThirtyDays, "30 Tage"),
        ];
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        JumpToLetterCommand = new RelayCommand<string>(RaiseJumpRequested);
        FilterByFolderCommand = new RelayCommand<string>(FilterByFolder);
        FilterByTagCommand = new RelayCommand<string>(FilterByTag);
        SelectPeriodCommand = new RelayCommand<AllFilesPeriod>(SelectPeriod);
        RemoveFilterCommand = new RelayCommand<ActiveFilterViewModel>(RemoveFilter);
        ResetSearchAndFiltersCommand = new RelayCommand(ResetSearchAndFilters);
    }

    /// <summary>Aktuelle (gefilterte + sortierte) Sicht auf die Datei-Liste.</summary>
    public ObservableCollection<AllFilesItemViewModel> Items { get; }

    /// <summary>Löst einen Lade-Roundtrip aus.</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Die Buchstaben der Sprungleiste — immer vollständig, nie gekürzt.</summary>
    public IReadOnlyList<JumpLetterViewModel> JumpLetters { get; } =
        [.. AlphabetIndex.Letters.Select(letter => new JumpLetterViewModel(letter))];

    /// <summary>Leert das Suchfeld.</summary>
    public RelayCommand ClearSearchCommand { get; }

    /// <summary>Springt zur Gruppe eines Buchstabens.</summary>
    public RelayCommand<string> JumpToLetterCommand { get; }

    /// <summary>Die wählbaren Änderungszeiträume — feste Menge, deshalb als Umschalter.</summary>
    public IReadOnlyList<PeriodFilterViewModel> PeriodFilters { get; }

    /// <summary>
    /// Die gerade wirkenden Filter, jeder einzeln entfernbar.
    /// </summary>
    /// <remarks>
    /// Ein Filter, der wirkt, aber nicht zu sehen ist, erzeugt den Eindruck fehlender
    /// Daten — und man sucht den Fehler dann in den Daten statt in der Einschränkung.
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

    /// <summary>Wird ausgelöst, wenn zu einer Buchstabengruppe gesprungen werden soll.</summary>
    public event Action<char>? JumpRequested;

    /// <summary>
    /// Ob die Sprungleiste angezeigt wird.
    /// </summary>
    /// <remarks>
    /// Nur bei alphabetischer Sortierung: Nach Datum sortiert stünde ein Sprung auf „M"
    /// an einer beliebigen Stelle der Liste. Und erst ab einer Bestandsgröße, ab der man
    /// die Liste nicht mehr überblickt — darunter ist die Leiste nur zusätzliche Fläche.
    /// </remarks>
    public bool IsJumpBarVisible => SortMode is AllFilesSortMode.Title or AllFilesSortMode.RelativePath
        && Items.Count >= JumpBarThreshold;

    /// <summary>Es gibt überhaupt keine Einträge — unabhängig von Suche und Filter.</summary>
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
    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        State = AllFilesListState.Loading;
        try
        {
            AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(true))
            {
                IAllFilesQuery query = scope.ServiceProvider.GetRequiredService<IAllFilesQuery>();
                IReadOnlyList<AllFilesRow> rows = await query.GetAllAsync(CancellationToken.None).ConfigureAwait(true);
                TimeZoneInfo timeZone = _timeProvider.LocalTimeZone;
                _allItems = rows.Select(row => new AllFilesItemViewModel(row, timeZone)).ToArray();
                ApplyViewState();
                LogLoaded(_logger, _allItems.Length);
            }
        }
        catch (InvalidOperationException ex)
        {
            // Der Fehler endet nicht im Log: Die Ansicht sagt es, sonst hält der Nutzer
            // seinen Bestand für leer und sucht am falschen Ende.
            LogRefreshFailed(_logger, ex);
            State = AllFilesListState.LoadFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool MatchesSearch(AllFilesItemViewModel item, string needle)
    {
        if (item.Title.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (item.RelativePath.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return item.TagSlugs.Any(slug => slug.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    [LoggerMessage(EventId = 1300, Level = LogLevel.Information, Message = "Alle-Dateien-Tab geladen — {Count} Einträge.")]
    private static partial void LogLoaded(ILogger logger, int count);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Warning, Message = "Alle-Dateien-Tab konnte nicht geladen werden.")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);

    partial void OnSearchTextChanged(string value)
    {
        ApplyViewState();
    }

    partial void OnSortModeChanged(AllFilesSortMode value)
    {
        ApplyViewState();
    }

    partial void OnSelectedItemChanged(AllFilesItemViewModel? value)
    {
        if (value is null)
        {
            return;
        }
        FileSelected?.Invoke(value.AbsolutePath);
    }

    private void ApplyViewState()
    {
        bool alphabetical = SortMode is AllFilesSortMode.Title or AllFilesSortMode.RelativePath;

        RebuildItems(SortedMatches(), alphabetical);
        UpdateJumpLetters(alphabetical);
        UpdateActiveFilters();

        // Ein Zustand, aus dem sich alles ableitet — statt mehrerer Merker, die
        // auseinanderlaufen können.
        State = (_allItems.Length, Items.Count) switch
        {
            (0, _) => AllFilesListState.EmptyStock,
            (_, 0) => AllFilesListState.NoMatches,
            _ => AllFilesListState.Items,
        };

        OnPropertyChanged(nameof(IsJumpBarVisible));
    }

    /// <summary>Die Einträge, die Suche und Filter passieren — in der gewählten Ordnung.</summary>
    private IEnumerable<AllFilesItemViewModel> SortedMatches()
    {
        string trimmed = SearchText?.Trim() ?? string.Empty;
        IEnumerable<AllFilesItemViewModel> filtered = string.IsNullOrEmpty(trimmed)
            ? _allItems
            : _allItems.Where(item => MatchesSearch(item, trimmed));

        filtered = filtered.Where(MatchesFilters);

        return SortMode switch
        {
            AllFilesSortMode.Title => filtered.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase),
            AllFilesSortMode.RelativePath => filtered.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase),
            _ => filtered.OrderByDescending(item => item.LastWriteTimeUtc).ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>Füllt die sichtbare Liste neu und beschriftet dabei die Gruppen.</summary>
    private void RebuildItems(IEnumerable<AllFilesItemViewModel> sorted, bool alphabetical)
    {
        Items.Clear();
        foreach (AllFilesItemViewModel item in sorted)
        {
            // Der Buchstabe folgt dem Schlüssel, nach dem sortiert wurde. Nach Datum
            // sortiert gibt es keinen sinnvollen Buchstaben — dann bleibt die Beschriftung
            // leer und die Ansicht zeigt keinen Gruppenkopf.
            item.GroupLabel = alphabetical
                ? AlphabetIndex.LetterOf(SortKeyOf(item)).ToString(CultureInfo.InvariantCulture)
                : string.Empty;
            Items.Add(item);
        }
    }

    partial void OnStateChanged(AllFilesListState value)
    {
        OnPropertyChanged(nameof(ShowsNothingAtAll));
        OnPropertyChanged(nameof(ShowsNoMatches));
        OnPropertyChanged(nameof(ShowsLoadFailure));
    }

    /// <summary>Ob ein Eintrag alle gesetzten Filter passiert.</summary>
    private bool MatchesFilters(AllFilesItemViewModel item)
    {
        if (_folderFilter is not null
            && !string.Equals(item.FolderPath, _folderFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Mehrere Kennzeichnungen wirken zusammen, nicht nebeneinander: Wer zwei anklickt,
        // erwartet die Schnittmenge, nicht die Summe.
        if (!_tagFilters.All(tag => item.TagSlugs.Contains(tag, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        return _period == AllFilesPeriod.Any || item.LastWriteTimeUtc >= EarliestOf(_period);
    }

    /// <summary>Der früheste Zeitpunkt, der im gewählten Zeitraum noch mitzählt.</summary>
    private DateTime EarliestOf(AllFilesPeriod period)
    {
        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        // Der laufende Tag zählt mit: „7 Tage" meint heute und die sechs davor, nicht acht.
        return period switch
        {
            AllFilesPeriod.Today => nowUtc.Date,
            AllFilesPeriod.LastSevenDays => nowUtc.Date.AddDays(-(SevenDayWindow - 1)),
            AllFilesPeriod.LastThirtyDays => nowUtc.Date.AddDays(-(ThirtyDayWindow - 1)),
            _ => DateTime.MinValue,
        };
    }

    /// <summary>Stellt die Chip-Leiste der wirkenden Filter neu zusammen.</summary>
    private void UpdateActiveFilters()
    {
        ActiveFilters.Clear();

        if (_folderFilter is not null)
        {
            string shown = _folderFilter.Length == 0 ? "(Wurzel)" : _folderFilter;
            ActiveFilters.Add(new ActiveFilterViewModel(AllFilesFilterKind.Folder, _folderFilter, $"Ordner: {shown}"));
        }

        foreach (string tag in _tagFilters.Order(StringComparer.OrdinalIgnoreCase))
        {
            ActiveFilters.Add(new ActiveFilterViewModel(AllFilesFilterKind.Tag, tag, $"Kennzeichnung: {tag}"));
        }

        if (_period != AllFilesPeriod.Any)
        {
            PeriodFilterViewModel chosen = PeriodFilters.First(filter => filter.Period == _period);
            ActiveFilters.Add(new ActiveFilterViewModel(AllFilesFilterKind.Period, chosen.Label, $"Geändert: {chosen.Label}"));
        }

        foreach (PeriodFilterViewModel filter in PeriodFilters)
        {
            filter.IsActive = filter.Period == _period;
        }
    }

    private void FilterByFolder(string? folderPath)
    {
        if (folderPath is null)
        {
            return;
        }

        _folderFilter = folderPath;
        ApplyViewState();
    }

    private void FilterByTag(string? tagSlug)
    {
        if (string.IsNullOrWhiteSpace(tagSlug) || !_tagFilters.Add(tagSlug))
        {
            return;
        }

        ApplyViewState();
    }

    private void SelectPeriod(AllFilesPeriod period)
    {
        if (_period == period)
        {
            return;
        }

        _period = period;
        ApplyViewState();
    }

    private void RemoveFilter(ActiveFilterViewModel? filter)
    {
        if (filter is null)
        {
            return;
        }

        switch (filter.Kind)
        {
            case AllFilesFilterKind.Folder:
                _folderFilter = null;
                break;
            case AllFilesFilterKind.Tag:
                _ = _tagFilters.Remove(filter.Value);
                break;
            case AllFilesFilterKind.Period:
                _period = AllFilesPeriod.Any;
                break;
            default:
                return;
        }

        ApplyViewState();
    }

    private void ResetSearchAndFilters()
    {
        _folderFilter = null;
        _tagFilters.Clear();
        _period = AllFilesPeriod.Any;

        // Setzt die Ansicht selbst dann neu zusammen, wenn der Suchtext schon leer war —
        // sonst bliebe das Zurücknehmen der Filter ohne Wirkung.
        if (SearchText.Length == 0)
        {
            ApplyViewState();
            return;
        }

        SearchText = string.Empty;
    }

    /// <summary>Der Wert, nach dem in der aktuellen Sortierung geordnet wird.</summary>
    private string SortKeyOf(AllFilesItemViewModel item) =>
        SortMode == AllFilesSortMode.RelativePath ? item.RelativePath : item.Title;

    /// <summary>
    /// Markiert, welche Buchstaben Einträge haben.
    /// </summary>
    /// <remarks>
    /// Buchstaben ohne Einträge werden deaktiviert, nicht entfernt: Eine Leiste, die ihre
    /// Breite je nach Bestand ändert, ist kein verlässlicher Anlaufpunkt.
    /// </remarks>
    private void UpdateJumpLetters(bool alphabetical)
    {
        HashSet<char> occupied = alphabetical
            ? [.. Items.Select(item => AlphabetIndex.LetterOf(SortKeyOf(item)))]
            : [];

        foreach (JumpLetterViewModel letter in JumpLetters)
        {
            letter.HasEntries = occupied.Contains(letter.Letter);
        }
    }

    private void RaiseJumpRequested(string? letter)
    {
        if (string.IsNullOrEmpty(letter))
        {
            return;
        }

        JumpRequested?.Invoke(letter[0]);
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

    /// <summary>Der Buchstabe als Text — für Anzeige und Befehlsparameter.</summary>
    public string Label { get; }
}

/// <summary>Was die Datei-Liste gerade zeigt.</summary>
/// <remarks>
/// Ein Zustand statt mehrerer bools nebeneinander: Zwei Merker, die sich widersprechen,
/// ergeben eine Ansicht, die es laut Code nicht geben kann — und genau die sieht dann der
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

/// <summary>Ein Filter, der gerade wirkt — als entfernbarer Chip dargestellt.</summary>
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

    /// <summary>Der eingestellte Wert — Ordner, Kennzeichnung oder Zeitraum.</summary>
    public string Value { get; }

    /// <summary>Die Aufschrift des Chips.</summary>
    public string Label { get; }

    /// <summary>Beschriftung der Entfernen-Schaltfläche, auch für die Sprachausgabe.</summary>
    public string RemoveHint => $"Filter „{Label}“ entfernen";
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

    /// <summary>Seit Mitternacht (UTC) geändert.</summary>
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
        FolderPath = FolderOf(row.RelativePath);
    }

    /// <summary>Stabiler Schlüssel.</summary>
    public Guid MarkdownFileId { get; }

    /// <summary>Dateiname ohne Erweiterung.</summary>
    public string Title { get; }

    /// <summary>Pfad relativ zum konfigurierten Root.</summary>
    public string RelativePath { get; }

    /// <summary>Vollqualifizierter Pfad — Eingabe für den Navigations-Locator.</summary>
    public string AbsolutePath { get; }

    /// <summary>Letzte Änderung auf Disk (UTC) — Grundlage für Sortierung und Zeitraumfilter.</summary>
    public DateTime LastWriteTimeUtc { get; }

    /// <summary>
    /// Dieselbe Änderung in der Zeitzone des Rechners. Nur diese wird angezeigt: Wer eine
    /// Datei gerade bearbeitet hat, erwartet die Uhrzeit seiner eigenen Uhr.
    /// </summary>
    public DateTime LastWriteTime { get; }

    /// <summary>Slugs der angewendeten Tags.</summary>
    public IReadOnlyList<string> TagSlugs { get; }

    /// <summary>
    /// Beschriftung der Gruppe, in der dieser Eintrag steht — leer, wenn nicht gruppiert wird.
    /// </summary>
    public string GroupLabel { get; internal set; } = string.Empty;

    /// <summary>Der Ordner relativ zur Wurzel; leer für Dateien, die direkt darin liegen.</summary>
    public string FolderPath { get; }

    /// <summary>
    /// Trennt den Ordneranteil eines relativen Pfads ab.
    /// </summary>
    /// <remarks>
    /// Beide Trennzeichen zählen: Der Index schreibt je nach Herkunft den einen oder den
    /// anderen, und ein Filter, der nur eines kennt, greift dann bei der Hälfte nicht.
    /// </remarks>
    private static string FolderOf(string relativePath)
    {
        int cut = relativePath.LastIndexOfAny(['/', '\\']);

        return cut < 0 ? string.Empty : relativePath[..cut].Replace('\\', '/');
    }
}
