using MdExplorer.App.Services;
using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Stellt Suchanfragen nach einer Wartezeit und liefert nur die Antwort der jüngsten zurück.
/// </summary>
/// <remarks>
/// <para>
/// Jede neue Anfrage verwirft den wartenden Zeitgeber und bricht die laufende Abfrage ab.
/// Gesucht wird im Hintergrund; die Antwort stellt sich am UI-Thread an und wird dort nur
/// zugestellt, wenn sie noch zur jüngsten Anfrage gehört. Prüfung und Zustellung liegen damit auf
/// demselben Thread wie jede neue Anfrage, und nichts kann dazwischenfallen.
/// </para>
/// <para>
/// Der <see cref="ISearchService"/> lebt im Scope; jeder Lauf holt ihn über einen eigenen Scope,
/// damit kein langlebiger Besitzer einen Datenbankkontext festhält.
/// </para>
/// </remarks>
internal sealed partial class DebouncedSearchRunner : IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger _logger;
    private readonly Action<IReadOnlyList<SearchResult>> _deliverResults;
    private readonly Action _deliverFailure;
    private readonly Lock _gate = new();

    private CancellationTokenSource? _currentRun;
    private ITimer? _timer;
    private bool _disposed;

    /// <summary>Erzeugt den Läufer.</summary>
    /// <param name="scopeFactory">Liefert je Lauf den Suchdienst.</param>
    /// <param name="timeProvider">Die Uhr der Wartezeit.</param>
    /// <param name="dispatcher">Stellt Antworten am UI-Thread zu.</param>
    /// <param name="logger">Protokoll des Besitzers.</param>
    /// <param name="deliverResults">Erhält die Treffer der jüngsten Anfrage, auf dem UI-Thread.</param>
    /// <param name="deliverFailure">Wird gerufen, wenn die jüngste Anfrage gescheitert ist, auf dem UI-Thread.</param>
    public DebouncedSearchRunner(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IUiDispatcher dispatcher,
        ILogger logger,
        Action<IReadOnlyList<SearchResult>> deliverResults,
        Action deliverFailure)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _dispatcher = dispatcher;
        _logger = logger;
        _deliverResults = deliverResults;
        _deliverFailure = deliverFailure;
    }

    /// <summary>Stellt <paramref name="query"/> nach <paramref name="delay"/>; eine wartende oder laufende Anfrage entfällt.</summary>
    public void Schedule(SearchQuery query, TimeSpan delay)
    {
        lock (_gate)
        {
            StopCurrent();
            if (!_disposed)
            {
                _timer = _timeProvider.CreateTimer(OnElapsed, query, delay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>Verwirft die wartende und die laufende Anfrage; ihre Antwort wird nicht mehr zugestellt.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            StopCurrent();
        }
    }

    /// <summary>Hält Zeitgeber und Abfrage an.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            StopCurrent();
        }
    }

    [LoggerMessage(EventId = 300, Level = LogLevel.Error, Message = "Suche fehlgeschlagen für Eingabe {QueryText}.")]
    private static partial void LogSearchFailure(ILogger logger, Exception exception, string queryText);

    private void OnElapsed(object? state)
    {
        if (state is not SearchQuery query)
        {
            return;
        }
        CancellationTokenSource run;
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            run = new CancellationTokenSource();
            token = run.Token;
            _currentRun = run;
        }
        ObservedTask.Forget(RunAsync(query, run, token), _logger);
    }

    private async Task RunAsync(SearchQuery query, CancellationTokenSource run, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<SearchResult> results = await SearchInScopeAsync(query, cancellationToken).ConfigureAwait(false);
            _dispatcher.Post(() => DeliverIfCurrent(run, () => _deliverResults(results)));
        }
        catch (OperationCanceledException)
        {
            // Durch eine neue Eingabe verdrängt, kein Fehler.
        }
        catch (Exception exception) when (DataLoadFailure.IsExpected(exception))
        {
            LogSearchFailure(_logger, exception, query.Text);
            _dispatcher.Post(() => DeliverIfCurrent(run, _deliverFailure));
        }
    }

    private async Task<IReadOnlyList<SearchResult>> SearchInScopeAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            ISearchService searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();
            return await searchService.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        }
    }

    // Läuft auf dem UI-Thread, wie jede neue Anfrage. Ist der Lauf nicht mehr der jüngste, hat der
    // Nutzer inzwischen weitergetippt oder das Feld geleert, und die Antwort fällt weg.
    private void DeliverIfCurrent(CancellationTokenSource run, Action deliver)
    {
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(run, _currentRun))
            {
                return;
            }
        }
        deliver();
    }

    private void StopCurrent()
    {
        _timer?.Dispose();
        _timer = null;
        CancellationTokenSource? run = _currentRun;
        _currentRun = null;
        if (run is null)
        {
            return;
        }
        run.Cancel();
        run.Dispose();
    }
}
