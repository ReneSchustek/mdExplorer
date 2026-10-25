using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Localization;
using MdExplorer.App.Logging;
using MdExplorer.App.Services;
using MdExplorer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Bindet den Log-Viewer an den In-Memory-Sink. Hält alle bekannten Einträge in einer
/// <see cref="ObservableCollection{T}"/> und zeigt über <see cref="FilteredView"/> die nach
/// Level, Suchtext und Zeitraum gefilterte Sicht (<see cref="LogEntryFilter"/>).
/// </summary>
/// <remarks>
/// Neue Einträge kommen auf dem schreibenden Thread an. Sie stellen sich am UI-Thread an, statt
/// ihn anzuhalten: Ein synchroner Sprung hielte jeden protokollierenden Thread fest, bis die
/// Oberfläche frei ist, und wartete die Oberfläche auf ihn, stünde beides. Was sich in der
/// Zwischenzeit sammelt, wird in einem Durchgang übernommen.
/// </remarks>
internal sealed partial class LogViewerViewModel : ObservableObject, IDisposable
{
    private readonly IMemoryLogStore _store;
    private readonly IUiDispatcher _dispatcher;
    private readonly IFileSaveDialogService _saveDialog;
    private readonly IFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LogViewerViewModel> _logger;
    private readonly ConcurrentQueue<LogEntry> _pending = new();

    // Einträge, die schon in der Momentaufnahme standen und trotzdem über das Abonnement kommen
    // können, weil es davor begonnen hat. Nach dem ersten Durchgang leer.
    private HashSet<LogEntry>? _fromSnapshot;
    private int _drainScheduled;

    // Erst nach der Momentaufnahme darf ein Durchgang Einträge übernehmen; ein früherer stellte
    // sie vor die älteren und ließe die doppelten durch.
    private bool _snapshotTaken;
    private int _visibleCount;
    private volatile bool _disposed;

    [ObservableProperty]
    private LogLevelFilter _selectedLevelFilter = LogEntryFilter.Levels[0];

    // Der Zeitraum zählt bis zum Moment der Auswahl oder des Eintreffens; ein Eintrag, der
    // währenddessen aus dem Zeitraum altert, verschwindet erst beim nächsten Filterwechsel.
    [ObservableProperty]
    private LogPeriodFilter _selectedPeriodFilter = LogEntryFilter.Periods[0];

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>Was der letzte Export bewirkt hat, in einem Satz; leer, solange keiner lief.</summary>
    [ObservableProperty]
    private string _exportMessage = string.Empty;

    /// <summary>
    /// Erstellt das ViewModel. <paramref name="store"/> wird abonniert,
    /// bis das ViewModel disposed wird.
    /// </summary>
    public LogViewerViewModel(
        IMemoryLogStore store,
        IUiDispatcher dispatcher,
        IFileSaveDialogService saveDialog,
        IFileSystem fileSystem,
        TimeProvider timeProvider,
        ILogger<LogViewerViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(saveDialog);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _dispatcher = dispatcher;
        _saveDialog = saveDialog;
        _fileSystem = fileSystem;
        _timeProvider = timeProvider;
        _logger = logger;

        Entries = [];
        FilteredView = CollectionViewSource.GetDefaultView(Entries);
        FilteredView.Filter = obj => obj is LogEntry entry && Matches(entry);

        // Erst abonnieren, dann die Momentaufnahme: Umgekehrt ginge verloren, was dazwischen
        // geschrieben wird. Was dabei doppelt ankommt, fällt im ersten Durchgang heraus.
        store.EntryAdded += OnEntryAdded;
        IReadOnlyList<LogEntry> snapshot = store.Snapshot();
        _fromSnapshot = new HashSet<LogEntry>(ReferenceEqualityComparer.Instance);
        foreach (LogEntry entry in snapshot)
        {
            _ = _fromSnapshot.Add(entry);
            Append(entry);
        }
        _snapshotTaken = true;
        DrainPending();
    }

    /// <summary>Backing-Collection für die Live-Log-Liste; geschrieben wird nur auf dem UI-Thread.</summary>
    public ObservableCollection<LogEntry> Entries { get; }

    /// <summary>Gefilterte Sicht auf <see cref="Entries"/>. Bindet die UI direkt.</summary>
    public ICollectionView FilteredView { get; }

    /// <summary>Auswahl-Tabelle für die Level-Filter-ComboBox.</summary>
    public IReadOnlyList<LogLevelFilter> LevelFilters { get; } = LogEntryFilter.Levels;

    /// <summary>Auswahl-Tabelle für den Zeitraumfilter.</summary>
    public IReadOnlyList<LogPeriodFilter> PeriodFilters { get; } = LogEntryFilter.Periods;

    /// <summary>Anzahl der nach Filter sichtbaren Einträge, für die Statuszeile.</summary>
    /// <remarks>
    /// Mitgezählt statt jedes Mal durch die Sicht gezählt: Bei einem vollen Puffer liefe sonst
    /// für jede neue Zeile eine Schleife über zweitausend Einträge.
    /// </remarks>
    public int VisibleCount => _visibleCount;

    /// <summary>Hebt die Abo-Bindung an den Sink wieder auf.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _store.EntryAdded -= OnEntryAdded;
    }

    private static string LevelToShort(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    private static void AppendLine(StringBuilder builder, LogEntry entry)
    {
        _ = builder.Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture));
        _ = builder.Append(" [");
        _ = builder.Append(LevelToShort(entry.Level));
        _ = builder.Append("] ");
        if (!string.IsNullOrEmpty(entry.SourceContext))
        {
            _ = builder.Append(entry.SourceContext);
            _ = builder.Append(": ");
        }
        _ = builder.AppendLine(entry.Message);
        if (entry.Exception is not null)
        {
            _ = builder.AppendLine(entry.Exception);
        }
    }

    [LoggerMessage(EventId = 1500, Level = LogLevel.Warning, Message = "Protokoll-Export nach {Path} fehlgeschlagen.")]
    private static partial void LogExportFailed(ILogger logger, string path, Exception exception);

    partial void OnSelectedLevelFilterChanged(LogLevelFilter value) => RefreshFilter();

    partial void OnSearchQueryChanged(string value) => RefreshFilter();

    partial void OnSelectedPeriodFilterChanged(LogPeriodFilter value) => RefreshFilter();

    private void RefreshFilter()
    {
        FilteredView.Refresh();
        _visibleCount = Entries.Count(Matches);
        OnPropertyChanged(nameof(VisibleCount));
    }

    /// <summary>Speichert die aktuell gefilterten Einträge als Text-Datei.</summary>
    [RelayCommand]
    private async Task ExportAsync(CancellationToken cancellationToken)
    {
        string defaultName = "mdexplorer-log-"
            + _timeProvider.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + ".log";
        string? path = _saveDialog.PromptForSavePath(defaultName, UiText.Get("LogViewer_Export_Filter"));
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        StringBuilder builder = new();
        foreach (LogEntry entry in FilteredView.OfType<LogEntry>())
        {
            AppendLine(builder, entry);
        }

        try
        {
            // UTF-8 ohne Byte-Order-Mark: So lesen es auch Werkzeuge, die die Markierung als Zeichen zeigen.
            byte[] content = new UTF8Encoding(false).GetBytes(builder.ToString());
            await _fileSystem.WriteAllBytesAtomicAsync(path, content, cancellationToken).ConfigureAwait(true);
            ExportMessage = UiText.Format("LogViewer_Export_Done", path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogExportFailed(_logger, path, exception);
            ExportMessage = UiText.Format("LogViewer_Export_Failed", exception.Message);
        }
    }

    private bool Matches(LogEntry entry) =>
        LogEntryFilter.Matches(entry, SelectedLevelFilter, SearchQuery, SelectedPeriodFilter, _timeProvider.GetUtcNow());

    // Läuft auf dem schreibenden Thread. Nur der erste Eintrag einer Folge stellt einen Durchgang
    // an; alle weiteren landen in derselben Warteschlange.
    private void OnEntryAdded(object? sender, LogEntry entry)
    {
        if (_disposed)
        {
            return;
        }
        _pending.Enqueue(entry);
        if (Interlocked.Exchange(ref _drainScheduled, 1) == 0)
        {
            _dispatcher.Post(DrainPending);
        }
    }

    private void DrainPending()
    {
        // Erst die Marke zurück, dann leeren: Ein Eintrag, der danach kommt, stellt einen neuen
        // Durchgang an, statt in einer schon geleerten Schlange liegen zu bleiben.
        _ = Interlocked.Exchange(ref _drainScheduled, 0);
        if (_disposed || !_snapshotTaken)
        {
            return;
        }
        HashSet<LogEntry>? fromSnapshot = _fromSnapshot;
        _fromSnapshot = null;
        bool changed = false;
        while (_pending.TryDequeue(out LogEntry? entry))
        {
            if (fromSnapshot?.Contains(entry) == true)
            {
                continue;
            }
            Append(entry);
            changed = true;
        }
        if (changed)
        {
            OnPropertyChanged(nameof(VisibleCount));
        }
    }

    private void Append(LogEntry entry)
    {
        Entries.Add(entry);
        if (Matches(entry))
        {
            _visibleCount++;
        }
        // Spiegelt die harte Ring-Buffer-Grenze des Sinks und bewahrt UI vor
        // unbegrenzter Liste, wenn das Fenster länger offen bleibt.
        while (Entries.Count > _store.Capacity)
        {
            if (Matches(Entries[0]))
            {
                _visibleCount--;
            }
            Entries.RemoveAt(0);
        }
    }
}
