using MdExplorer.App.Localization;
using MdExplorer.App.Logging;
using MdExplorer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Services;

/// <summary>
/// Bewertet den Live-Log-Puffer (<see cref="IMemoryLogStore"/>), den Stand der nicht
/// verarbeitbaren Dateien (<see cref="IParseFailureStatus"/>) und die angehaltenen
/// Hintergrunddienste (<see cref="IBackgroundServiceStatus"/>) und leitet daraus den
/// aggregierten <see cref="OperationHealth"/>-Status ab. Reagiert auf jedes neue Log-Event
/// und auf jede Änderung der beiden Stände; feuert <see cref="Changed"/> nur bei
/// tatsächlicher Stand-Änderung; UI-Bindings updaten dadurch nicht unnötig.
/// </summary>
internal sealed class OperationHealthProvider : IOperationHealthProvider, IDisposable
{
    // Nur die jüngsten Einträge zählen: Ein Fehler hält die Anzeige rot, bis 200 neuere
    // Einträge ihn aus dem Fenster schieben, statt sie für die ganze Sitzung zu färben.
    private const int RelevantWindow = 200;

    private readonly IMemoryLogStore _store;
    private readonly IParseFailureStatus _parseFailureStatus;
    private readonly IBackgroundServiceStatus _serviceStatus;
    private readonly object _gate = new();
    private OperationHealth _current = OperationHealth.Healthy;
    private string _detail = UiText.Get("Health_Healthy");
    private bool _disposed;

    /// <summary>Erzeugt den Provider und abonniert Sink und Fehlschlag-Stand.</summary>
    public OperationHealthProvider(IMemoryLogStore store, IParseFailureStatus parseFailureStatus, IBackgroundServiceStatus serviceStatus)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(parseFailureStatus);
        ArgumentNullException.ThrowIfNull(serviceStatus);
        _store = store;
        _parseFailureStatus = parseFailureStatus;
        _serviceStatus = serviceStatus;
        Reevaluate();
        _store.EntryAdded += OnEntryAdded;
        _parseFailureStatus.Changed += OnStatusChanged;
        _serviceStatus.Changed += OnStatusChanged;
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
        _parseFailureStatus.Changed -= OnStatusChanged;
        _serviceStatus.Changed -= OnStatusChanged;
    }

    /// <summary>Leitet aus der Fenster-Statistik den aggregierten Status und den Anzeigetext ab.</summary>
    private static (OperationHealth Status, string Detail) DetermineStatus(LogWindowStats stats)
    {
        if (stats.ErrorCount > 0 && stats.LastError is not null)
        {
            return (OperationHealth.Error, UiText.Format("Health_Errors", stats.ErrorCount)
                + "\n" + UiText.Format("Health_LastError", stats.LastError.Message));
        }
        if (stats.WarningCount > 0 && stats.LastWarning is not null)
        {
            return (OperationHealth.Warning, UiText.Format("Health_Warnings", stats.WarningCount)
                + "\n" + UiText.Format("Health_LastWarning", stats.LastWarning.Message));
        }
        return (OperationHealth.Healthy, UiText.Get("Health_Healthy"));
    }

    /// <summary>Zählt Fehler/Warnungen im jüngsten Beobachtungsfenster und merkt sich das jeweils letzte Event.</summary>
    private static LogWindowStats CountRecent(IReadOnlyList<LogEntry> recent)
    {
        int errorCount = 0;
        int warningCount = 0;
        LogEntry? lastError = null;
        LogEntry? lastWarning = null;
        foreach (LogEntry candidate in recent)
        {
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
            ? UiText.Get("Health_UnparsableFiles_One")
            : UiText.Format("Health_UnparsableFiles_Many", unparsableFileCount);
        OperationHealth status = logStatus == OperationHealth.Error ? OperationHealth.Error : OperationHealth.Warning;
        string detail = logStatus == OperationHealth.Healthy ? unparsableLine : logDetail + "\n" + unparsableLine;
        return (status, detail);
    }

    /// <summary>
    /// Ein angehaltener Hintergrunddienst hält den Stand auf „Fehler“, bis die Anwendung neu
    /// startet. Anders als ein Protokolleintrag rollt er nicht aus dem Beobachtungsfenster.
    /// </summary>
    private static (OperationHealth Status, string Detail) IncludeStoppedServices(
        OperationHealth status,
        string detail,
        IReadOnlyCollection<BackgroundServiceKind> stopped)
    {
        if (stopped.Count == 0)
        {
            return (status, detail);
        }

        string names = string.Join(", ", stopped.Select(NameOf));
        string line = UiText.Format("Health_ServiceStopped", names);
        return (OperationHealth.Error, status == OperationHealth.Healthy ? line : line + "\n" + detail);
    }

    private static string NameOf(BackgroundServiceKind service) => service switch
    {
        BackgroundServiceKind.Indexer => UiText.Get("Health_Service_Indexer"),
        BackgroundServiceKind.Parser => UiText.Get("Health_Service_Parser"),
        BackgroundServiceKind.SearchIndex => UiText.Get("Health_Service_SearchIndex"),
        BackgroundServiceKind.TagCloud => UiText.Get("Health_Service_TagCloud"),
        _ => service.ToString(),
    };

    private void OnEntryAdded(object? sender, LogEntry entry) => Reevaluate();

    private void OnStatusChanged(object? sender, EventArgs args) => Reevaluate();

    /// <summary>
    /// Liest die Stände, bewertet sie und übernimmt das Ergebnis; feuert <see cref="Changed"/> nur
    /// bei echter Änderung.
    /// </summary>
    /// <remarks>
    /// Lesen und Übernehmen stehen unter derselben Sperre. Protokollieren zwei Threads zugleich,
    /// könnte sonst der, der früher gelesen hat, seinen Stand nach dem des anderen setzen, und ein
    /// Fehler im Fenster stünde als grün da. Das Ereignis feuert außerhalb der Sperre, weil seine
    /// Empfänger auf den UI-Thread springen.
    /// </remarks>
    private void Reevaluate()
    {
        bool changed;
        lock (_gate)
        {
            (OperationHealth logStatus, string logDetail) = DetermineStatus(CountRecent(_store.Recent(RelevantWindow)));
            (OperationHealth withFiles, string filesDetail) = IncludeUnparsableFiles(
                logStatus, logDetail, _parseFailureStatus.UnparsableFileCount);
            (OperationHealth status, string detail) = IncludeStoppedServices(withFiles, filesDetail, _serviceStatus.Stopped);
            changed = _current != status || !string.Equals(_detail, detail, StringComparison.Ordinal);
            _current = status;
            _detail = detail;
        }
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Zählung über das Beobachtungsfenster samt dem jeweils jüngsten Fehler und der jüngsten Warnung.</summary>
    private readonly record struct LogWindowStats(int ErrorCount, int WarningCount, LogEntry? LastError, LogEntry? LastWarning);
}
