using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.Services;
using MdExplorer.Core.Messaging;
using MdExplorer.Indexer.Abstractions;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Die Statuszeile des Hauptfensters: wie viele Dateien indiziert sind, wann der Indexer zuletzt
/// etwas gemeldet hat und wie es um den Betrieb steht.
/// </summary>
/// <remarks>
/// Indexer und Betriebsanzeige melden sich auf Hintergrund-Threads. Ihre Meldungen werden hinter
/// die Arbeit des UI-Threads gestellt statt ihn anzuhalten: Wartete der Indexer bei jeder Meldung
/// auf die Oberfläche, bremste die Anzeige den Lauf, den sie beschreibt.
/// </remarks>
internal sealed partial class StatusBarViewModel : ObservableObject, IDisposable, IRecipient<SettingsChangedMessage>
{
    private readonly IOperationHealthProvider _healthProvider;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IIndexer _indexer;
    private readonly IMessenger _messenger;
    private readonly TimeProvider _timeProvider;
    private readonly IndexProgressTally _tally = new();
    private bool _disposed;

    [ObservableProperty]
    private int _indexedFileCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastIndexerProgress))]
    private DateTime? _lastIndexerProgressUtc;

    [ObservableProperty]
    private OperationHealth _health;

    [ObservableProperty]
    private string _healthDetail;

    /// <summary>Verbindet die Statuszeile mit Indexer und Betriebsanzeige.</summary>
    public StatusBarViewModel(MainServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _healthProvider = services.HealthProvider;
        _uiDispatcher = services.UiDispatcher;
        _indexer = services.Indexer;
        _messenger = services.Messenger;
        _timeProvider = services.TimeProvider;
        _health = _healthProvider.Current;
        _healthDetail = _healthProvider.Detail;

        _healthProvider.Changed += OnHealthChanged;
        _indexer.InitialScanProgress += OnIndexerProgress;
        _messenger.Register(this);
    }

    /// <summary>
    /// Die letzte Fortschrittsmeldung des Indexers in der Zeitzone des Rechners. Intern wird in
    /// UTC gerechnet, abgelesen wird aber an der Uhr, die neben dem Bildschirm hängt.
    /// </summary>
    public DateTime? LastIndexerProgress => LastIndexerProgressUtc is DateTime utc
        ? TimeZoneInfo.ConvertTimeFromUtc(utc, _timeProvider.LocalTimeZone)
        : null;

    /// <summary>
    /// Vergisst den Indexstand von Wurzeln, die der Nutzer entfernt hat; sonst zählten sie in der
    /// Statuszeile weiter mit.
    /// </summary>
    public void Receive(SettingsChangedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        int total = _tally.Retain(message.Current.Indexing.Roots);
        _uiDispatcher.Post(() => IndexedFileCount = total);
    }

    /// <summary>Trennt die Abonnements.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _messenger.Unregister<SettingsChangedMessage>(this);
        _healthProvider.Changed -= OnHealthChanged;
        _indexer.InitialScanProgress -= OnIndexerProgress;
    }

    private void OnIndexerProgress(object? sender, IndexerScanProgressEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        int totalProcessed = _tally.Record(args.Root, args.ProcessedCount);
        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        _uiDispatcher.Post(() =>
        {
            IndexedFileCount = totalProcessed;
            LastIndexerProgressUtc = nowUtc;
        });
    }

    private void OnHealthChanged(object? sender, EventArgs args)
    {
        _uiDispatcher.Post(() =>
        {
            Health = _healthProvider.Current;
            HealthDetail = _healthProvider.Detail;
        });
    }
}
