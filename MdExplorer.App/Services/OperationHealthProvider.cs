using System.Globalization;
using MdExplorer.App.Logging;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Services;

/// <summary>
/// Bewertet den Live-Log-Puffer (<see cref="IMemoryLogStore"/>) und leitet daraus
/// den aggregierten <see cref="OperationHealth"/>-Status ab. Reagiert auf jedes
/// neue Log-Event und feuert <see cref="Changed"/> nur bei tatsaechlicher
/// Stand-Aenderung — UI-Bindings updaten dadurch nicht unnoetig.
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

    private void OnEntryAdded(object? sender, LogEntry entry) => Reevaluate();

    private void Reevaluate()
    {
        IReadOnlyList<LogEntry> snapshot = _store.Snapshot();
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

        OperationHealth status;
        string detail;
        if (errorCount > 0 && lastError is not null)
        {
            status = OperationHealth.Error;
            detail = string.Create(
                CultureInfo.InvariantCulture,
                $"{errorCount} Fehler im letzten Beobachtungsfenster.\nLetzter: {lastError.Message}");
        }
        else if (warningCount > 0 && lastWarning is not null)
        {
            status = OperationHealth.Warning;
            detail = string.Create(
                CultureInfo.InvariantCulture,
                $"{warningCount} Warnung(en) im letzten Beobachtungsfenster.\nLetzte: {lastWarning.Message}");
        }
        else
        {
            status = OperationHealth.Healthy;
            detail = "Alle Subsysteme laufen normal.";
        }

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
}
