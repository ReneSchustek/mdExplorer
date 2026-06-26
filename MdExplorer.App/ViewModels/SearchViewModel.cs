using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;
using MdExplorer.TagCloud.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// ViewModel der mittleren Spalte. Debouncing der Eingabe, asynchrone Suche und
/// Pflege der Trefferliste. Concurrency wird über einen verworfenen <see cref="CancellationTokenSource"/>
/// realisiert: jede neue Eingabe bricht die laufende Abfrage ab. Der Zugriff auf den Scoped
/// <see cref="ISearchService"/> erfolgt pro Suchlauf über einen eigenen DI-Scope, damit das
/// Singleton-ViewModel kein Captive-DbContext-Antipattern erzeugt.
/// </summary>
internal sealed partial class SearchViewModel : ObservableObject, IDisposable,
    IRecipient<TagClickedMessage>
{
    /// <summary>Standard-Debounce-Zeit für Tipp-Eingaben.</summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(200);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _debounce;
    private readonly ILogger<SearchViewModel> _logger;
    private readonly IMessenger _messenger;
    private readonly Lock _gate = new();
    // Object-Lock (kein System.Threading.Lock!) — EnableCollectionSynchronization erfordert
    // einen kompatiblen Monitor-Lock fuer das WPF-Binding (Memory: wpf_collection_sync_lock).
    private readonly object _resultsGate = new();

    private CancellationTokenSource? _currentRunCts;
    private ITimer? _debounceTimer;
    private bool _disposed;

    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private SearchResultItemViewModel? _selectedResult;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private SearchMode _mode = SearchMode.Fts5;

    [ObservableProperty]
    private SimilarityMode _similarity = SimilarityMode.None;

    // Standard: globale Suche ueber alle Roots. Erst wenn der Nutzer den Scope aktiviert,
    // wird die Trefferliste auf den im Ordnerbaum gewaehlten Pfad (PathPrefixFilter) beschnitten.
    [ObservableProperty]
    private bool _scopeToSelectedFolder;

    /// <summary>Standard-Konstruktor: nutzt <see cref="DefaultDebounce"/>.</summary>
    public SearchViewModel(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, IMessenger messenger, ILogger<SearchViewModel> logger)
        : this(scopeFactory, timeProvider, messenger, logger, DefaultDebounce)
    {
    }

    /// <summary>Konstruktor mit anpassbarem Debounce — für Tests.</summary>
    internal SearchViewModel(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, IMessenger messenger, ILogger<SearchViewModel> logger, TimeSpan debounce)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(debounce, TimeSpan.Zero);

        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _messenger = messenger;
        _logger = logger;
        _debounce = debounce;
        Results = [];
        // Cross-Thread-Schreiben in Results: der Debounce-Timer feuert auf ThreadPool, das
        // anschliessende ApplyResults muss aber an die UI-bound ListBox kommen. Ohne diese
        // Registrierung fliegt eine NotSupportedException ("CollectionView aus Nicht-Dispatcher-
        // Thread"), die aus dem RunSearchAsync-Catch herausfaellt (NotSupportedException ist
        // keine InvalidOperationException) und die Trefferliste leer bleiben laesst.
        BindingOperations.EnableCollectionSynchronization(Results, _resultsGate);
        _messenger.RegisterAll(this);
    }

    /// <summary>Optionaler Pfad-Prefix-Filter, der vom <see cref="FolderTreeViewModel"/> gespeist wird.</summary>
    public string? PathPrefixFilter { get; set; }

    /// <summary>Sortierte Trefferliste — UI-Binding-Ziel.</summary>
    public ObservableCollection<SearchResultItemViewModel> Results { get; }

    /// <summary>Wird gefeuert, wenn der Such-Lauf abgeschlossen ist (für Tests).</summary>
    public event EventHandler? SearchCompleted;

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
        _messenger.UnregisterAll(this);
        DisposeTimer();
        CancelInFlight();
    }

    /// <summary>
    /// Empfänger von Tag-Klick-Events aus der Tag-Cloud. Setzt den Such-Filter
    /// gemäß <see cref="TagFilterMode"/>: <see cref="TagFilterMode.Replace"/> ersetzt die Anfrage
    /// durch <c>tag:slug</c>, <see cref="TagFilterMode.Add"/> hängt sie an, <see cref="TagFilterMode.Exclude"/>
    /// hängt <c>-tag:slug</c> an. Doppelte Tokens werden idempotent vermieden.
    /// </summary>
    public void Receive(TagClickedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(message.Slug))
        {
            return;
        }
        string token = $"tag:{message.Slug}";
        string excludeToken = $"-tag:{message.Slug}";
        QueryText = message.Mode switch
        {
            TagFilterMode.Replace => token,
            TagFilterMode.Exclude => AppendUnique(QueryText, excludeToken),
            TagFilterMode.Add => AppendUnique(QueryText, token),
            _ => token,
        };
    }

    private static string AppendUnique(string existing, string token)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return token;
        }
        string trimmed = existing.Trim();
        string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Any(part => string.Equals(part, token, StringComparison.Ordinal)))
        {
            return trimmed;
        }
        return trimmed + " " + token;
    }

    partial void OnQueryTextChanged(string value)
    {
        ScheduleDebouncedSearch(value);
    }

    partial void OnModeChanged(SearchMode value)
    {
        if (!string.IsNullOrWhiteSpace(QueryText))
        {
            ScheduleDebouncedSearch(QueryText);
        }
    }

    partial void OnSimilarityChanged(SimilarityMode value)
    {
        if (!string.IsNullOrWhiteSpace(QueryText))
        {
            ScheduleDebouncedSearch(QueryText);
        }
    }

    partial void OnScopeToSelectedFolderChanged(bool value)
    {
        if (!string.IsNullOrWhiteSpace(QueryText))
        {
            ScheduleDebouncedSearch(QueryText);
        }
    }

    private void ScheduleDebouncedSearch(string queryText)
    {
        lock (_gate)
        {
            DisposeTimer();
            CancelInFlight();
            if (string.IsNullOrWhiteSpace(queryText))
            {
                Results.Clear();
                return;
            }
            _debounceTimer = _timeProvider.CreateTimer(OnDebounceElapsed, queryText, _debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnDebounceElapsed(object? state)
    {
        if (state is not string queryText)
        {
            return;
        }
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _currentRunCts = new CancellationTokenSource();
            cts = _currentRunCts;
        }
        _ = RunSearchAsync(queryText, cts.Token);
    }

    private async Task RunSearchAsync(string queryText, CancellationToken cancellationToken)
    {
        IsSearching = true;
        try
        {
            SearchQuery query = new(queryText, Mode: Mode, Similarity: Similarity);
            IReadOnlyList<SearchResult> results = await ExecuteSearchInScopeAsync(query, cancellationToken).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            ApplyResults(results);
        }
        catch (OperationCanceledException)
        {
            // Suchlauf wurde durch neue Eingabe verdrängt — kein Fehler.
        }
        catch (InvalidOperationException exception)
        {
            LogSearchFailure(_logger, exception, queryText);
        }
        finally
        {
            IsSearching = false;
            SearchCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<IReadOnlyList<SearchResult>> ExecuteSearchInScopeAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(true))
        {
            ISearchService searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();
            return await searchService.SearchAsync(query, cancellationToken).ConfigureAwait(true);
        }
    }

    private void ApplyResults(IReadOnlyList<SearchResult> incoming)
    {
        // Nur scopen, wenn der Nutzer es aktiv eingeschaltet hat — sonst bleibt die Suche global.
        string? prefix = ScopeToSelectedFolder ? PathPrefixFilter : null;
        // Lock-Pflicht: EnableCollectionSynchronization erwartet, dass jeder Schreiber den
        // gleichen Monitor-Lock haelt — sonst kann WPF beim Reverse-Read (Dispatcher-Thread)
        // einen inkonsistenten Mittelzustand sehen.
        lock (_resultsGate)
        {
            Results.Clear();
            foreach (SearchResult result in incoming)
            {
                if (!MatchesPathFilter(result, prefix))
                {
                    continue;
                }
                Results.Add(new SearchResultItemViewModel(result));
            }
        }
    }

    private static bool MatchesPathFilter(SearchResult result, string? prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return true;
        }
        return result.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private void DisposeTimer()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = null;
    }

    private void CancelInFlight()
    {
        CancellationTokenSource? cts = _currentRunCts;
        _currentRunCts = null;
        if (cts is null)
        {
            return;
        }
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // CTS war bereits beim Run-Ende disposed — kein Fehler.
        }
        cts.Dispose();
    }

    [LoggerMessage(EventId = 300, Level = LogLevel.Error, Message = "Suche fehlgeschlagen für Eingabe {QueryText}.")]
    private static partial void LogSearchFailure(ILogger logger, Exception exception, string queryText);
}
