using System.Globalization;
using MdExplorer.App.Logging;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Services;

/// <summary>
/// Bewertet den Live-Log-Puffer (<see cref="IMemoryLogStore"/>) und leitet daraus
/// den aggregierten <see cref="OperationHealth"/>-Status ab. Reagiert auf jedes
/// neue Log-Event und feuert <see cref="Changed"/> nur bei tatsächlicher
/// Stand-Änderung — UI-Bindings updaten dadurch nicht unnötig.
/// </summary>
internal sealed class OperationHealthProvider : IOperationHealthProvider, IDisposable
{
    private const int RelevantWindow = 200;

    private readonly IMemoryLogStore _store;
    private readonly object _gate = new();
    private OperationHealth _current = OperationHealth.Healthy;
    private string _detail = "Alle Subsysteme laufen normal.";
    private bool _disposed;

    /// <summary>Erzeugt den Provider und abonniert den Sink.</summary>
    public OperationHealthProvider(IMemoryLogStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        Reevaluate();
        _store.EntryAdded += OnEntryAdded;
    }

    /// <inheritdoc />
    public OperationHealth Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <inheritdoc />
    public string Detail
    {
        get
        {
            lock (_gate)
            {
                return _detail;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>Hebt die Abo-Bindung auf.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _store.EntryAdded -= OnEntryAdded;
    }

    /// <summary>Leitet aus der Fenster-Statistik den aggregierten Status und den Anzeigetext ab.</summary>
    private static (OperationHealth Status, string Detail) DetermineStatus(LogWindowStats stats)
    {
        if (stats.ErrorCount > 0 && stats.LastError is not null)
        {
            return (OperationHealth.Error, string.Create(
                CultureInfo.InvariantCulture,
                $"{stats.ErrorCount} Fehler im letzten Beobachtungsfenster.\nLetzter: {stats.LastError.Message}"));
        }
        if (stats.WarningCount > 0 && stats.LastWarning is not null)
        {
            return (OperationHealth.Warning, string.Create(
                CultureInfo.InvariantCulture,
                $"{stats.WarningCount} Warnung(en) im letzten Beobachtungsfenster.\nLetzte: {stats.LastWarning.Message}"));
        }
        return (OperationHealth.Healthy, "Alle Subsysteme laufen normal.");
    }

    /// <summary>Zählt Fehler/Warnungen im jüngsten Beobachtungsfenster und merkt sich das jeweils letzte Event.</summary>
    private static LogWindowStats CountRecent(IReadOnlyList<LogEntry> snapshot)
    {
        int start = Math.Max(0, snapshot.Count - RelevantWindow);
        int errorCount = 0;
        int warningCount = 0;
        LogEntry? lastError = null;
        LogEntry? lastWarning = null;
        for (int i = start; i < snapshot.Count; i++)
        {
            LogEntry candidate = snapshot[i];
            if (candidate.Level >= LogLevel.Error)
            {
                errorCount++;
                lastError = candidate;
            }
            else if (candidate.Level == LogLevel.Warning)
            {
                warningCount++;
                lastWarning = candidate;
            }
        }
        return new LogWindowStats(errorCount, warningCount, lastError, lastWarning);
    }

    private void OnEntryAdded(object? sender, LogEntry entry) => Reevaluate();

    private void Reevaluate()
    {
        LogWindowStats stats = CountRecent(_store.Snapshot());
        (OperationHealth status, string detail) = DetermineStatus(stats);
        ApplyStatus(status, detail);
    }

    /// <summary>Übernimmt den neuen Stand unter Lock und feuert <see cref="Changed"/> nur bei echter Änderung.</summary>
    private void ApplyStatus(OperationHealth status, string detail)
    {
        bool changed;
        lock (_gate)
        {
            changed = _current != status || !string.Equals(_detail, detail, StringComparison.Ordinal);
            _current = status;
            _detail = detail;
        }
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private readonly record struct LogWindowStats(int ErrorCount, int WarningCount, LogEntry? LastError, LogEntry? LastWarning);
}
