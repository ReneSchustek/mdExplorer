using MdExplorer.App.ViewModels;
using MdExplorer.Core.Hosting;
using MdExplorer.Indexer.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Services;

/// <summary>
/// Reicht Indexer-Fortschritts-Events an das „Alle Dateien"-ViewModel weiter, damit
/// dessen Liste während des Initial-Scans inkrementell befüllt wird. Ohne diese
/// Brücke bliebe der Tab leer, bis der gesamte Scan durchgelaufen ist;
/// auf großen Roots (mehrere Tausend Dateien) sind das mehrere Minuten.
/// </summary>
internal sealed partial class IndexerProgressBridge : IHostedService, IDisposable
{
    private readonly IIndexer _indexer;
    private readonly AllFilesViewModel _allFiles;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<IndexerProgressBridge> _logger;
    private bool _disposed;

    /// <summary>Erzeugt die Brücke und löst Abhängigkeiten auf.</summary>
    public IndexerProgressBridge(
        IIndexer indexer,
        AllFilesViewModel allFiles,
        IUiDispatcher dispatcher,
        ILogger<IndexerProgressBridge> logger)
    {
        ArgumentNullException.ThrowIfNull(indexer);
        ArgumentNullException.ThrowIfNull(allFiles);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(logger);

        _indexer = indexer;
        _allFiles = allFiles;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _indexer.InitialScanProgress += OnInitialScanProgress;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _indexer.InitialScanProgress -= OnInitialScanProgress;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _indexer.InitialScanProgress -= OnInitialScanProgress;
    }

    [LoggerMessage(EventId = 920, Level = LogLevel.Debug, Message = "Indexer-Fortschritt: {Root} → {Count} Dateien (abgeschlossen: {Completed}).")]
    private static partial void LogProgress(ILogger logger, string root, int count, bool completed);

    [LoggerMessage(EventId = 921, Level = LogLevel.Warning, Message = "Auto-Refresh des Alle-Dateien-Tabs ist fehlgeschlagen.")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);

    private void OnInitialScanProgress(object? sender, IndexerScanProgressEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        LogProgress(_logger, args.Root, args.ProcessedCount, args.IsCompleted);
        // Anstellen statt warten: Das Ereignis kommt vom Indexer-Thread, und der soll nicht
        // stehen, bis die Oberfläche geladen hat. Meldungen während eines Ladens merkt sich die
        // Liste und lädt danach einmal nach.
        _dispatcher.Post(() => _ = RefreshSafelyAsync());
    }

    private async Task RefreshSafelyAsync()
    {
        try
        {
            await _allFiles.RefreshAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Erwartet: Shutdown reißt den Refresh ab; nicht loggen.
        }
        catch (Exception ex) when (BackgroundServiceWatchdog.IsRecoverable(ex, CancellationToken.None))
        {
            // Die Brücke ist die letzte Auffangstelle: Ein Fehler hier bliebe sonst als
            // unbeobachtete Ausnahme liegen. Speichermangel und Stapelüberlauf gehen weiter.
            LogRefreshFailed(_logger, ex);
        }
    }
}
