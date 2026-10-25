using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.Services;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Messaging;
using MdExplorer.Core.Models;
using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;
using MdExplorer.TagCloud.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>Was die Trefferliste gerade zeigt.</summary>
/// <remarks>
/// Vier Lagen, die eine leere Liste alle gleich aussehen lässt: noch nichts eingegeben,
/// gerade unterwegs, ohne Treffer zurück, oder gescheitert. Ein Zustand statt mehrerer
/// Merker hält sie auseinander; die Anzeige-Eigenschaften leiten sich hieraus ab.
/// </remarks>
internal enum SearchListState
{
    /// <summary>Es steht keine Anfrage im Feld.</summary>
    Idle = 0,

    /// <summary>Eine Anfrage ist gestellt, die Antwort steht aus.</summary>
    Searching = 1,

    /// <summary>Die Antwort ist da und enthält nichts.</summary>
    NoMatches = 2,

    /// <summary>Es gibt Treffer.</summary>
    Results = 3,

    /// <summary>Die Suche selbst ist gescheitert.</summary>
    Failed = 4,
}

/// <summary>
/// ViewModel des Registers Suche: stellt die Anfrage aus Eingabe, Modus, Ordner und Seitengröße
/// zusammen und pflegt die Trefferliste.
/// </summary>
/// <remarks>
/// Die Anfrage wird beim Tippen auf dem UI-Thread zusammengestellt. Entprellen, Abbrechen und das
/// Zustellen nur der jüngsten Antwort übernimmt <see cref="DebouncedSearchRunner"/>.
/// </remarks>
internal sealed partial class SearchViewModel : ObservableObject, IDisposable,
    IRecipient<TagClickedMessage>,
    IRecipient<SettingsChangedMessage>
{
    private readonly IMessenger _messenger;
    private readonly DebouncedSearchRunner _runner;
    private readonly Lock _gate = new();
    private bool _disposed;

    /// <summary>
    /// Wartezeit nach dem letzten Tastenanschlag, bevor gesucht wird. Ohne sie liefe für jedes
    /// getippte Zeichen eine eigene Abfrage an, die die nächste gleich wieder abbricht. Der Wert
    /// kommt aus den Einstellungen und ändert sich mit ihnen; gelesen und geschrieben wird er
    /// unter <see cref="_gate"/>, wie <see cref="_resultsPerPage"/>.
    /// </summary>
    private TimeSpan _debounce;

    /// <summary>Wie viele Treffer eine Anfrage holt, aus der Einstellung „Treffer pro Seite“.</summary>
    private int _resultsPerPage;

    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private SearchResultItemViewModel? _selectedResult;

    /// <summary>Der Zustand, auf den die Ansicht reagiert, die einzige Quelle dafür.</summary>
    [ObservableProperty]
    private SearchListState _state = SearchListState.Idle;

    [ObservableProperty]
    private SearchMode _mode = SearchMode.Fts5;

    [ObservableProperty]
    private SimilarityMode _similarity = SimilarityMode.None;

    // Standard: globale Suche über alle Roots. Erst wenn der Nutzer den Scope aktiviert,
    // beschränkt die Anfrage sich auf den im Ordnerbaum gewählten Pfad (PathPrefixFilter).
    [ObservableProperty]
    private bool _scopeToSelectedFolder;

    /// <summary>Der im Ordnerbaum gewählte Ordner, relativ zur Wurzel; gilt nur bei eingeschalteter Beschränkung.</summary>
    [ObservableProperty]
    private string? _pathPrefixFilter;

    /// <summary>Erzeugt das ViewModel mit Wartezeit und Seitengröße aus den Einstellungen.</summary>
    public SearchViewModel(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IMessenger messenger,
        ISettingsService settings,
        IUiDispatcher dispatcher,
        ILogger<SearchViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(logger);

        _messenger = messenger;
        _runner = new DebouncedSearchRunner(scopeFactory, timeProvider, dispatcher, logger, ApplyResults, () => State = SearchListState.Failed);
        TakeSettings(settings.Current);
        Results = [];
        ClearSearchCommand = new RelayCommand(Clear);
        _messenger.RegisterAll(this);
    }

    /// <summary>Leert Eingabe und Trefferliste.</summary>
    public RelayCommand ClearSearchCommand { get; }

    /// <summary>
    /// Es wurde noch gar nicht gesucht.
    /// </summary>
    /// <remarks>
    /// Der Fall, den eine Dateiliste nicht kennt: „noch nichts eingegeben" ist etwas anderes
    /// als „nichts gefunden". Wer beides gleich beschriftet, lässt den Nutzer glauben, sein
    /// Bestand sei leer.
    /// </remarks>
    public bool ShowsNothingSearchedYet => State == SearchListState.Idle;

    /// <summary>Es wurde gesucht, und die Anfrage hat nichts getroffen.</summary>
    /// <remarks>
    /// Erst nach einer Antwort, nicht schon beim Tippen: Sonst stünde zwischen Anschlag und
    /// Ergebnis für einen Wimpernschlag „nichts gefunden" auf dem Schirm.
    /// </remarks>
    public bool ShowsNoMatches => State == SearchListState.NoMatches;

    /// <summary>Die Suche selbst ist gescheitert.</summary>
    /// <remarks>
    /// Ohne eigenen Zustand endete ein Fehlschlag still im Protokoll, und auf dem Schirm
    /// stünde „nichts gefunden", dieselbe Aussage wie bei einer Anfrage ohne Treffer, obwohl
    /// die Suche gar nicht gelaufen ist.
    /// </remarks>
    public bool ShowsSearchFailure => State == SearchListState.Failed;

    /// <summary>Eine Anfrage ist unterwegs und trägt die Fortschrittsanzeige.</summary>
    /// <remarks>
    /// Bewusst abgeleitet und nicht als eigener Merker geführt: Sonst gäbe es zwei Stellen,
    /// die dasselbe behaupten, und die eine läuft irgendwann der anderen davon. Der Zustand
    /// beginnt schon mit dem Anschlag, nicht erst mit der Abfrage; die kurze Wartezeit bis
    /// dahin gehört aus Sicht des Nutzers zur Suche.
    /// </remarks>
    public bool IsSearching => State == SearchListState.Searching;

    /// <summary>Sortierte Trefferliste (UI-Binding-Ziel); geschrieben wird nur auf dem UI-Thread.</summary>
    public ObservableCollection<SearchResultItemViewModel> Results { get; }

    /// <summary>Löscht die Eingabe und die Trefferliste.</summary>
    public void Clear()
    {
        QueryText = string.Empty;
    }

    /// <summary>Stoppt den Debounce-Timer und entsorgt Ressourcen.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _runner.Dispose();
        _messenger.UnregisterAll(this);
    }

    /// <summary>
    /// Empfänger von Tag-Klick-Events aus der Tag-Cloud. Setzt den Such-Filter
    /// gemäß <see cref="TagFilterMode"/>, siehe <see cref="SearchQueryText.WithTag"/>.
    /// </summary>
    public void Receive(TagClickedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(message.Slug))
        {
            return;
        }
        QueryText = SearchQueryText.WithTag(QueryText, message.Slug, message.Mode);
    }

    /// <summary>
    /// Übernimmt Wartezeit und Seitengröße aus dem Einstellungsdialog. Sie gelten ab der nächsten
    /// Eingabe; ein bereits laufender Zeitgeber endet mit der Zeit, mit der er angelegt wurde.
    /// </summary>
    public void Receive(SettingsChangedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        TakeSettings(message.Current);
    }

    private void TakeSettings(AppSettings settings)
    {
        TimeSpan debounce = TimeSpan.FromMilliseconds(settings.Behavior.SearchDebounceMs);
        ArgumentOutOfRangeException.ThrowIfLessThan(debounce, TimeSpan.Zero);
        lock (_gate)
        {
            _debounce = debounce;
            _resultsPerPage = settings.Appearance.ResultsPerPage;
        }
    }

    partial void OnQueryTextChanged(string value) => ScheduleDebouncedSearch(value);

    partial void OnModeChanged(SearchMode value) => RepeatSearch();

    partial void OnSimilarityChanged(SimilarityMode value) => RepeatSearch();

    partial void OnScopeToSelectedFolderChanged(bool value) => RepeatSearch();

    // Ein anderer Ordner ändert die Anfrage nur, wenn die Beschränkung gilt; sonst bliebe nach
    // dem Wechsel die Trefferliste des vorigen Ordners stehen.
    partial void OnPathPrefixFilterChanged(string? value)
    {
        if (ScopeToSelectedFolder)
        {
            RepeatSearch();
        }
    }

    partial void OnStateChanged(SearchListState value)
    {
        OnPropertyChanged(nameof(ShowsNothingSearchedYet));
        OnPropertyChanged(nameof(ShowsNoMatches));
        OnPropertyChanged(nameof(ShowsSearchFailure));
        OnPropertyChanged(nameof(IsSearching));
    }

    private void RepeatSearch()
    {
        if (!string.IsNullOrWhiteSpace(QueryText))
        {
            ScheduleDebouncedSearch(QueryText);
        }
    }

    private void ScheduleDebouncedSearch(string queryText)
    {
        bool isBlank = string.IsNullOrWhiteSpace(queryText);
        if (isBlank)
        {
            _runner.Cancel();
            Results.Clear();
        }
        else
        {
            (SearchQuery query, TimeSpan debounce) = BuildQuery(queryText);
            _runner.Schedule(query, debounce);
        }

        // Bewusst außerhalb der Sperre: Ein Empfänger von PropertyChanged darf zurückrufen,
        // ohne dass daraus ein Verklemmen wird. Und bis die Antwort da ist, wird über die
        // neue Eingabe nichts behauptet. Die vorige Antwort galt der vorigen Anfrage.
        State = isBlank ? SearchListState.Idle : SearchListState.Searching;
    }

    /// <summary>Stellt die Anfrage zusammen, solange alle Eingaben auf dem UI-Thread stehen.</summary>
    /// <remarks>
    /// Die Ordnerbeschränkung geht als <c>path:</c>-Filter in die Abfrage. Hinterher im Speicher
    /// gefiltert, blieben nur die Treffer, die zufällig unter den global besten lagen.
    /// </remarks>
    private (SearchQuery Query, TimeSpan Debounce) BuildQuery(string queryText)
    {
        string text = ScopeToSelectedFolder && !string.IsNullOrWhiteSpace(PathPrefixFilter)
            ? queryText + " " + SearchQueryText.PathFilter(PathPrefixFilter)
            : queryText;

        lock (_gate)
        {
            return (new SearchQuery(text, Take: _resultsPerPage, Mode: Mode, Similarity: Similarity), _debounce);
        }
    }

    private void ApplyResults(IReadOnlyList<SearchResult> incoming)
    {
        Results.Clear();
        foreach (SearchResult result in incoming)
        {
            Results.Add(new SearchResultItemViewModel(result));
        }

        // Ab hier steht eine Antwort zur aktuellen Eingabe. Erst jetzt darf der Leerzustand
        // „nichts gefunden" sagen.
        State = Results.Count == 0 ? SearchListState.NoMatches : SearchListState.Results;
    }
}
