using System.Globalization;
using MdExplorer.App.Logging;
using MdExplorer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Services;

/// <summary>
/// Bewertet den Live-Log-Puffer (<see cref="IMemoryLogStore"/>) und den Stand der nicht
/// verarbeitbaren Dateien (<see cref="IParseFailureStatus"/>) und leitet daraus den
/// aggregierten <see cref="OperationHealth"/>-Status ab. Reagiert auf jedes neue Log-Event
/// und auf jede Änderung des Fehlschlag-Standes; feuert <see cref="Changed"/> nur bei
/// tatsächlicher Stand-Änderung — UI-Bindings updaten dadurch nicht unnötig.
/// </summary>
internal sealed class OperationHealthProvider : IOperationHealthProvider, IDisposable
{
    private const int RelevantWindow = 200;

    private const string HealthyDetail = "Alle Subsysteme laufen normal.";

    private readonly IMemoryLogStore _store;
    private readonly IParseFailureStatus _parseFailureStatus;
    private readonly object _gate = new();
    private OperationHealth _current = OperationHealth.Healthy;
    private string _detail = HealthyDetail;
    private bool _disposed;

    /// <summary>Erzeugt den Provider und abonniert Sink und Fehlschlag-Stand.</summary>
    public OperationHealthProvider(IMemoryLogStore store, IParseFailureStatus parseFailureStatus)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(parseFailureStatus);
        _store = store;
        _parseFailureStatus = parseFailureStatus;
        Reevaluate();
        _store.EntryAdded += OnEntryAdded;
        _parseFailureStatus.Changed += OnParseFailureStatusChanged;
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

    /// <summary>Hebt die Abo-Bindungen auf.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _store.EntryAdded -= OnEntryAdded;
        _parseFailureStatus.Changed -= OnParseFailureStatusChanged;
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
        return (OperationHealth.Healthy, HealthyDetail);
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

    /// <summary>
    /// Hebt den Stand auf mindestens „Warnung" an, solange Dateien nicht verarbeitet werden
    /// können. Ohne diese Zeile verschwindet eine dauerhaft unparsbare Datei aus dem Blick:
    /// Ihr Fehlschlag wird nur noch einmal protokolliert und rollt danach aus der Datei heraus.
    /// </summary>
    private static (OperationHealth Status, string Detail) IncludeUnparsableFiles(
        OperationHealth logStatus,
        string logDetail,
        int unparsableFileCount)
    {
        if (unparsableFileCount <= 0)
        {
            return (logStatus, logDetail);
        }

        string unparsableLine = unparsableFileCount == 1
            ? "1 Datei nicht verarbeitbar."
            : string.Create(CultureInfo.InvariantCulture, $"{unparsableFileCount} Dateien nicht verarbeitbar.");
        OperationHealth status = logStatus == OperationHealth.Error ? OperationHealth.Error : OperationHealth.Warning;
        string detail = logStatus == OperationHealth.Healthy ? unparsableLine : logDetail + "\n" + unparsableLine;
        return (status, detail);
    }

    private void OnEntryAdded(object? sender, LogEntry entry) => Reevaluate();

    private void OnParseFailureStatusChanged(object? sender, EventArgs args) => Reevaluate();

    private void Reevaluate()
    {
        LogWindowStats stats = CountRecent(_store.Snapshot());
        (OperationHealth logStatus, string logDetail) = DetermineStatus(stats);
        (OperationHealth status, string detail) = IncludeUnparsableFiles(
            logStatus, logDetail, _parseFailureStatus.UnparsableFileCount);
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
