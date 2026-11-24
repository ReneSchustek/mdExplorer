using System.Data.Common;
using System.Threading.Channels;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Hosting;
using MdExplorer.Indexer.Abstractions;
using MdExplorer.Indexer.Models;
using MdExplorer.Indexer.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MdExplorer.Indexer.Services;

/// <summary>
/// Treibt den Indizierungs-Lebenszyklus: Erst-Scan, Watcher-Ereignisse, periodischer Abgleich.
/// Läuft als <see cref="BackgroundService"/>.
/// </summary>
/// <remarks>
/// Alle drei Wege schreiben in denselben Bestand und laufen deshalb nacheinander, nie
/// nebeneinander. Ohne den gegenseitigen Ausschluss legten Erst-Scan und Ereignis dieselbe neue
/// Datei zweimal an, und ein Abgleich, der seinen Ordner schon gelesen hatte, entfernte eine Datei
/// wieder, die der Ereigniskonsum gerade geschrieben hatte. Die Einstellungsänderung startet einen
/// weiteren Scan; auch er wartet, bis der laufende fertig ist.
/// </remarks>
public sealed partial class MarkdownIndexer : BackgroundService, IIndexer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileSystem _fileSystem;
    private readonly FileWatcherCoordinator _coordinator;
    private readonly ISettingsService _settings;
    private readonly IndexerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IBackgroundServiceStatus _serviceStatus;
    private readonly ILogger<MarkdownIndexer> _logger;
    private readonly RootSynchronizer _synchronizer;
    private readonly FileEventProcessor _eventProcessor;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    // Start und Umstellung lesen die Wurzeln und geben sie an die Watcher unter dieser Sperre weiter.
    // Der Dienst startet auf einem eigenen Thread; ohne sie konnte eine Änderung zwischen dem Lesen
    // der Wurzeln beim Start und dem Starten der Watcher verloren gehen.
    private readonly Lock _rootsLock = new();

    // Die Wurzeln, über die der Abgleich-Takt geht. Eine Änderung der Einstellungen ersetzt sie; der
    // Takt liest sie bei jedem Schlag neu, statt die Liste vom Start festzuhalten.
    private volatile IReadOnlyList<string> _liveRoots = [];

    /// <summary>Erzeugt den Indexer und löst alle Abhängigkeiten auf.</summary>
    public MarkdownIndexer(
        IServiceScopeFactory scopeFactory,
        IFileScanner scanner,
        IHashCalculator hashCalculator,
        IFileSystem fileSystem,
        FileWatcherCoordinator coordinator,
        ISettingsService settings,
        IOptions<IndexerOptions> options,
        TimeProvider timeProvider,
        IBackgroundServiceStatus serviceStatus,
        ILogger<MarkdownIndexer> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(hashCalculator);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _fileSystem = fileSystem;
        _coordinator = coordinator;
        _settings = settings;
        _options = options.Value;
        _timeProvider = timeProvider;
        ArgumentNullException.ThrowIfNull(serviceStatus);
        _serviceStatus = serviceStatus;
        _logger = logger;
        IndexEntryWriter writer = new(hashCalculator, fileSystem, timeProvider, logger);
        _synchronizer = new RootSynchronizer(scopeFactory, scanner, writer, _options, RaiseProgress, logger);
        _eventProcessor = new FileEventProcessor(scanner, writer, fileSystem, logger);
    }

    /// <inheritdoc />
    public event EventHandler<IndexerScanProgressEventArgs>? InitialScanProgress;

    /// <inheritdoc />
    /// <remarks>
    /// Läuft auch nach einer Änderung der Wurzeln. Watcher und Abgleich-Takt folgen dann der neuen
    /// Liste, und die Einträge entfernter Wurzeln verschwinden aus dem Index. Vorher kam nach dem
    /// Eintragen der ersten Wurzel bis zum Neustart keine Änderung mehr an, und eine entfernte
    /// Wurzel blieb beobachtet und durchsuchbar.
    /// </remarks>
    public async Task RunInitialScanAsync(CancellationToken cancellationToken)
    {
        List<string> roots;
        lock (_rootsLock)
        {
            roots = ValidRoots();
            _liveRoots = roots;
            _coordinator.Retarget(roots);
        }
        await RemoveOutsideConfiguredRootsAsync(cancellationToken).ConfigureAwait(false);
        await SyncRootsAsync(roots, SyncMode.Initial, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _writeGate.Dispose();
        base.Dispose();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            List<string> roots;
            lock (_rootsLock)
            {
                roots = ValidRoots();
                _liveRoots = roots;
                _coordinator.Start(roots);
            }
            if (roots.Count == 0)
            {
                // Der Dienst läuft trotzdem weiter: Trägt der Nutzer eine Wurzel ein, folgen
                // Watcher und Abgleich ihr ohne Neustart.
                LogNoRoots(_logger);
            }
            LogIndexerStarted(_logger, roots.Count);
            await RemoveOutsideConfiguredRootsAsync(stoppingToken).ConfigureAwait(false);
            await SyncRootsAsync(roots, SyncMode.Initial, stoppingToken).ConfigureAwait(false);
            await RunLiveAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Erwartetes Ende: Cancellation ist die normale Abbruchsemantik.
        }
        catch (Exception ex) when (BackgroundServiceWatchdog.IsRecoverable(ex, stoppingToken))
        {
            // Was bis hierher durchschlägt, beendet den Dienst geordnet, statt den Prozess mit einer
            // unbehandelten Ausnahme abzureißen.
            LogWatchdogTriggered(_logger, ex);
            _serviceStatus.ReportStopped(BackgroundServiceKind.Indexer);
        }
        finally
        {
            await _coordinator.StopAsync(CancellationToken.None).ConfigureAwait(false);
            LogIndexerStopped(_logger);
        }
    }

    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "Indexer gestartet — {RootCount} Wurzel(n).")]
    private static partial void LogIndexerStarted(ILogger logger, int rootCount);

    [LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "Indexer gestoppt.")]
    private static partial void LogIndexerStopped(ILogger logger);

    [LoggerMessage(EventId = 102, Level = LogLevel.Warning, Message = "Keine gültigen Indexer-Wurzeln konfiguriert — Indexer wartet auf eine Wurzel.")]
    private static partial void LogNoRoots(ILogger logger);

    [LoggerMessage(EventId = 103, Level = LogLevel.Warning, Message = "Konfigurierte Wurzel existiert nicht: {Root}")]
    private static partial void LogRootMissing(ILogger logger, string root);

    [LoggerMessage(EventId = 104, Level = LogLevel.Information, Message = "Durchgang über {RootCount} Wurzel(n) abgeschlossen — {Count} Datei(en) in {ElapsedMs} ms.")]
    private static partial void LogScanCompleted(ILogger logger, int rootCount, int count, double elapsedMs);

    [LoggerMessage(EventId = 113, Level = LogLevel.Warning, Message = "Indexer-Fortschritts-Event konnte nicht zugestellt werden.")]
    private static partial void LogProgressDispatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 114, Level = LogLevel.Warning, Message = "Indexer-Stapel fehlgeschlagen — Schleife läuft weiter.")]
    private static partial void LogProcessBatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 115, Level = LogLevel.Error, Message = "MarkdownIndexer-Watchdog: unerwartete Exception aufgefangen, Service wird ordentlich beendet.")]
    private static partial void LogWatchdogTriggered(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 120, Level = LogLevel.Warning, Message = "Abgleich der Wurzel {Root} wegen eines Datenbankfehlers abgebrochen — nächster Durchgang versucht es erneut.")]
    private static partial void LogRootSyncFailed(ILogger logger, string root, Exception exception);

    [LoggerMessage(EventId = 121, Level = LogLevel.Information, Message = "{Count} Einträge außerhalb der eingetragenen Wurzeln aus dem Index entfernt.")]
    private static partial void LogRemovedOutsideRoots(ILogger logger, int count);

    [LoggerMessage(EventId = 122, Level = LogLevel.Warning, Message = "Einträge außerhalb der eingetragenen Wurzeln ließen sich nicht entfernen — der nächste Durchgang versucht es erneut.")]
    private static partial void LogRemoveOutsideRootsFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 123, Level = LogLevel.Warning, Message = "Abgleich der Wurzel {Root} im Takt fehlgeschlagen — der nächste Schlag versucht es erneut.")]
    private static partial void LogResyncFailed(ILogger logger, string root, Exception exception);

    private List<string> ValidRoots()
    {
        List<string> result = [];
        foreach (string root in _settings.Current.Indexing.Roots.Where(root => !string.IsNullOrWhiteSpace(root)))
        {
            string normalized = Path.GetFullPath(root);
            if (!_fileSystem.DirectoryExists(normalized))
            {
                LogRootMissing(_logger, normalized);
                continue;
            }
            // In der Schreibweise der Platte, damit derselbe Ordner nie unter zwei Schreibweisen
            // in die Datenbank kommt (siehe RootPathCasing).
            result.Add(RootPathCasing.Canonicalize(_fileSystem, normalized));
        }
        return result;
    }

    private async Task SyncRootsAsync(List<string> roots, SyncMode mode, CancellationToken cancellationToken)
    {
        long startedAt = _timeProvider.GetTimestamp();
        int found = 0;
        foreach (string root in roots)
        {
            found += await SyncRootAsync(root, mode, cancellationToken).ConfigureAwait(false);
        }
        double elapsedMs = _timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;
        LogScanCompleted(_logger, roots.Count, found, elapsedMs);
    }

    /// <summary>Gleicht eine Wurzel unter der Schreibsperre ab.</summary>
    /// <remarks>
    /// Eine belegte Datenbank kostet diese Wurzel, nicht den Durchgang und nicht den Dienst. Der
    /// Erst-Scan meldet die Wurzel trotzdem als fertig, damit die Oberfläche nicht auf sie wartet.
    /// </remarks>
    private async Task<int> SyncRootAsync(string root, SyncMode mode, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _synchronizer.SyncAsync(root, mode, cancellationToken).ConfigureAwait(false);
        }
        catch (DbException ex)
        {
            LogRootSyncFailed(_logger, root, ex);
            if (mode == SyncMode.Initial)
            {
                RaiseProgress(new IndexerScanProgressEventArgs(root, 0, isCompleted: true));
            }
            return 0;
        }
        finally
        {
            _ = _writeGate.Release();
        }
    }

    // Ereigniskonsum und Abgleich-Takt laufen nebeneinander und enden gemeinsam: Endet der eine,
    // wird der andere abgebrochen und abgewartet, damit keine Aufgabe nach dem Ende des Dienstes
    // noch Scopes eines freigegebenen Containers benutzt.
    private async Task RunLiveAsync(CancellationToken stoppingToken)
    {
        using CancellationTokenSource live = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        Task resyncLoop = RunResyncLoopAsync(live.Token);
        try
        {
            await ConsumeEventsAsync(_coordinator.Events, live.Token).ConfigureAwait(false);
        }
        finally
        {
            await live.CancelAsync().ConfigureAwait(false);
            await resyncLoop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private async Task ConsumeEventsAsync(ChannelReader<FileSystemEvent> reader, CancellationToken cancellationToken)
    {
        List<FileSystemEvent> batch = new(_options.BatchSize);
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (batch.Count < _options.BatchSize && reader.TryRead(out FileSystemEvent? fileSystemEvent))
            {
                batch.Add(fileSystemEvent);
            }
            await ProcessBatchAsync(batch, cancellationToken).ConfigureAwait(false);
            batch.Clear();
        }
    }

    private async Task ProcessBatchAsync(IReadOnlyList<FileSystemEvent> batch, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<string> rootsToSync = [];
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                IMarkdownFileRepository repository = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();
                rootsToSync = await _eventProcessor.ApplyAsync(repository, batch, cancellationToken).ConfigureAwait(false);
                _ = await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (BackgroundServiceWatchdog.IsRecoverable(ex, cancellationToken))
        {
            // Ein Fehler in einem Stapel (Datenbank-Spitze, IO-Fehler, kaputtes Ereignis) bricht den
            // Konsum nicht ab. Die Wurzeln des Stapels werden gleich abgeglichen; auf den Takt
            // allein ist kein Verlass, er lässt sich abschalten. Ohne den Abgleich würde eine
            // verlorene Umbenennung beim nächsten Start zu Löschen und Anlegen, und Kennung und
            // Schlagworte der Datei gingen verloren.
            LogProcessBatchFailed(_logger, ex);
            rootsToSync = [.. batch.Select(fileSystemEvent => fileSystemEvent.Root).Distinct(StringComparer.OrdinalIgnoreCase)];
        }
        finally
        {
            _ = _writeGate.Release();
        }

        // Nach dem Freigeben der Sperre: Der Abgleich nimmt sie selbst.
        foreach (string root in rootsToSync)
        {
            _ = await SyncRootAsync(root, SyncMode.Background, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunResyncLoopAsync(CancellationToken cancellationToken)
    {
        TimeSpan resyncInterval = TimeSpan.FromSeconds(_settings.Current.Behavior.IndexerResyncIntervalSeconds);
        if (resyncInterval <= TimeSpan.Zero)
        {
            return;
        }

        using PeriodicTimer timer = new(resyncInterval, _timeProvider);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (string root in _liveRoots)
            {
                await TryResyncRootAsync(root, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // Ein Fehler kostet diesen Schlag für diese Wurzel, nicht den Takt. Vorher beendete jede
    // Ausnahme außer einem Datenbankfehler den Takt ohne Protokolleintrag bis zum Neustart.
    private async Task TryResyncRootAsync(string root, CancellationToken cancellationToken)
    {
        try
        {
            _ = await SyncRootAsync(root, SyncMode.Background, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (BackgroundServiceWatchdog.IsRecoverable(ex, cancellationToken))
        {
            LogResyncFailed(_logger, root, ex);
        }
    }

    /// <summary>Entfernt die Einträge, die unter keiner eingetragenen Wurzel mehr liegen.</summary>
    /// <remarks>
    /// Maßgeblich sind die eingetragenen Wurzeln, nicht nur die vorhandenen: Ein abgezogenes
    /// Laufwerk oder ein gerade fehlendes Netzlaufwerk kostet so nicht seinen ganzen Index.
    /// </remarks>
    private async Task RemoveOutsideConfiguredRootsAsync(CancellationToken cancellationToken)
    {
        List<string> configured =
        [
            .. _settings.Current.Indexing.Roots
                .Where(root => !string.IsNullOrWhiteSpace(root))
                .Select(Path.GetFullPath),
        ];
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                IMarkdownFileRepository repository = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();
                IReadOnlyList<Guid> outside = await repository.GetIdsOutsideRootsAsync(configured, cancellationToken).ConfigureAwait(false);
                if (outside.Count > 0)
                {
                    int removed = await repository.RemoveRangeAsync(outside, cancellationToken).ConfigureAwait(false);
                    LogRemovedOutsideRoots(_logger, removed);
                }
            }
        }
        catch (DbException ex)
        {
            // Der nächste Start oder die nächste Änderung der Wurzeln versucht es erneut.
            LogRemoveOutsideRootsFailed(_logger, ex);
        }
        finally
        {
            _ = _writeGate.Release();
        }
    }

    private void RaiseProgress(IndexerScanProgressEventArgs progress)
    {
        try
        {
            InitialScanProgress?.Invoke(this, progress);
        }
        catch (InvalidOperationException ex)
        {
            // Ein Marshalling-Fehler der Oberfläche darf den Scan nicht abbrechen.
            LogProgressDispatchFailed(_logger, ex);
        }
    }
}
