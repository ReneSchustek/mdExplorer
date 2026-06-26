using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Logging;
using MdExplorer.App.Services;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Bindet den Log-Viewer an den In-Memory-Sink an. Hält eine
/// <see cref="ObservableCollection{T}"/> mit allen aktuell bekannten Einträgen
/// und stellt über <see cref="FilteredView"/> eine durch Level + Suchtext
/// gefilterte Sicht bereit. Marshalled neue Einträge auf den UI-Thread.
/// </summary>
internal sealed partial class LogViewerViewModel : ObservableObject, IDisposable
{
    private readonly IMemoryLogStore _store;
    private readonly IUiDispatcher _dispatcher;
    private readonly IFileSaveDialogService _saveDialog;
    private readonly object _entriesLock = new();
    private bool _disposed;

    /// <summary>
    /// Erstellt das ViewModel. <paramref name="store"/> wird abonniert,
    /// bis das ViewModel disposed wird.
    /// </summary>
    public LogViewerViewModel(
        IMemoryLogStore store,
        IUiDispatcher dispatcher,
        IFileSaveDialogService saveDialog)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(saveDialog);

        _store = store;
        _dispatcher = dispatcher;
        _saveDialog = saveDialog;

        Entries = [];
        BindingOperations.EnableCollectionSynchronization(Entries, _entriesLock);

        foreach (LogEntry entry in store.Snapshot())
        {
            Entries.Add(entry);
        }

        FilteredView = CollectionViewSource.GetDefaultView(Entries);
        FilteredView.Filter = obj => obj is LogEntry entry && Matches(entry);

        store.EntryAdded += OnEntryAdded;
    }

    /// <summary>Backing-Collection für die Live-Log-Liste.</summary>
    public ObservableCollection<LogEntry> Entries { get; }

    /// <summary>Gefilterte Sicht auf <see cref="Entries"/> — Bindet die UI direkt.</summary>
    public ICollectionView FilteredView { get; }

    /// <summary>Auswahl-Tabelle für die Level-Filter-ComboBox.</summary>
    public IReadOnlyList<LogLevelFilter> LevelFilters { get; } = LogLevelFilterValues;

    private static readonly IReadOnlyList<LogLevelFilter> LogLevelFilterValues =
    [
        new(LogLevel.Trace, "Alle"),
        new(LogLevel.Debug, "Debug+"),
        new(LogLevel.Information, "Information+"),
        new(LogLevel.Warning, "Warnung+"),
        new(LogLevel.Error, "Fehler+"),
        new(LogLevel.Critical, "Kritisch"),
    ];

    [ObservableProperty]
    private LogLevelFilter _selectedLevelFilter = LogLevelFilterValues[0];

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>Anzahl der nach Filter sichtbaren Einträge — für die Statuszeile.</summary>
    public int VisibleCount
    {
        get
        {
            int count = 0;
            foreach (object? _ in FilteredView)
            {
                count++;
            }
            return count;
        }
    }

    partial void OnSelectedLevelFilterChanged(LogLevelFilter value)
    {
        FilteredView.Refresh();
        OnPropertyChanged(nameof(VisibleCount));
    }

    partial void OnSearchQueryChanged(string value)
    {
        FilteredView.Refresh();
        OnPropertyChanged(nameof(VisibleCount));
    }

    /// <summary>Speichert die aktuell gefilterten Einträge als Text-Datei.</summary>
    [RelayCommand]
    private async Task ExportAsync(CancellationToken cancellationToken)
    {
        string defaultName = "mdexplorer-log-"
            + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + ".log";
        string? path = _saveDialog.PromptForSavePath(defaultName, "Log-Datei (*.log)|*.log|Text (*.txt)|*.txt");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        StringBuilder builder = new();
        foreach (object? item in FilteredView)
        {
            if (item is not LogEntry entry)
            {
                continue;
            }
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

        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(true);
    }

    private bool Matches(LogEntry entry)
    {
        if (entry.Level < SelectedLevelFilter.MinimumLevel)
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            return true;
        }
        return entry.Message.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
            || entry.SourceContext.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase);
    }

    private void OnEntryAdded(object? sender, LogEntry entry)
    {
        if (_disposed)
        {
            return;
        }
        _dispatcher.Invoke(() => AppendEntry(entry));
    }

    private void AppendEntry(LogEntry entry)
    {
        Entries.Add(entry);
        // Spiegelt die harte Ring-Buffer-Grenze des Sinks — bewahrt UI vor
        // unbegrenzter Liste, wenn das Fenster länger offen bleibt.
        while (Entries.Count > _store.Capacity)
        {
            Entries.RemoveAt(0);
        }
        OnPropertyChanged(nameof(VisibleCount));
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
}

/// <summary>Auswahleintrag der Level-Filter-ComboBox.</summary>
/// <param name="MinimumLevel">Untere Grenze — Einträge mit niedrigerem Level werden ausgeblendet.</param>
/// <param name="Display">Anzeige-Label.</param>
internal sealed record LogLevelFilter(LogLevel MinimumLevel, string Display);
