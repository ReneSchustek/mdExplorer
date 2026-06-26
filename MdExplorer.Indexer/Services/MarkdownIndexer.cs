using System.Data.Common;
using System.Linq;
using System.Threading.Channels;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Hosting;
using MdExplorer.Core.Models;
using MdExplorer.Indexer.Abstractions;
using MdExplorer.Indexer.Models;
using MdExplorer.Indexer.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MdExplorer.Indexer.Services;

/// <summary>
/// Treibt den Indizierungs-Lebenszyklus: initialer Scan, Live-Watcher-Konsum,
/// periodischer Soll/Ist-Abgleich. Läuft als <see cref="BackgroundService"/>.
/// </summary>
public sealed partial class MarkdownIndexer : BackgroundService, IIndexer
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileScanner _scanner;
    private readonly IHashCalculator _hashCalculator;
    private readonly IFileSystem _fileSystem;
    private readonly FileWatcherCoordinator _coordinator;
    private readonly ISettingsService _settings;
    private readonly IndexerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MarkdownIndexer> _logger;

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
        _scanner = scanner;
        _hashCalculator = hashCalculator;
        _fileSystem = fileSystem;
        _coordinator = coordinator;
        _settings = settings;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        List<string> roots = ValidRoots();
        if (roots.Count == 0)
        {
            LogNoRoots(_logger);
            return;
        }

        _coordinator.Start(roots);
        LogIndexerStarted(_logger, roots.Count);

        try
        {
            await RunInitialScanInternalAsync(roots, stoppingToken).ConfigureAwait(false);

            Task resyncLoop = RunResyncLoopAsync(roots, stoppingToken);
            await ConsumeEventsAsync(_coordinator.Events, stoppingToken).ConfigureAwait(false);
            await resyncLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Erwartetes Ende — Cancellation ist die normale Abbruchsemantik.
        }
        catch (Exception ex) when (BackgroundServiceWatchdog.IsRecoverable(ex))
        {
            // Letzte Schicht: faengt unerwartete Exceptions (DbException aus
            // RunInitialScanInternalAsync, Library-Fehler aus ConsumeEventsAsync) damit der
            // Host den Service ordentlich beendet statt unhandled-Crash.
            LogWatchdogTriggered(_logger, ex);
        }
        finally
        {
            await _coordinator.StopAsync(CancellationToken.None).ConfigureAwait(false);
            LogIndexerStopped(_logger);
        }
    }

    /// <inheritdoc />
    public async Task RunInitialScanAsync(CancellationToken cancellationToken)
    {
        List<string> roots = ValidRoots();
        await RunInitialScanInternalAsync(roots, cancellationToken).ConfigureAwait(false);
    }

    private List<string> ValidRoots()
    {
        List<string> result = [];
        foreach (string root in _settings.Current.Indexing.Roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }
            string normalized = Path.GetFullPath(root);
            if (!_fileSystem.DirectoryExists(normalized))
            {
                LogRootMissing(_logger, normalized);
                continue;
            }
            result.Add(normalized);
        }
        return result;
    }

    private async Task RunInitialScanInternalAsync(IReadOnlyList<string> roots, CancellationToken cancellationToken)
    {
        long startedAt = _timeProvider.GetTimestamp();
        int totalProcessed = 0;
        foreach (string root in roots)
        {
            totalProcessed += await SyncRootAsync(root, cancellationToken).ConfigureAwait(false);
        }
        TimeSpan elapsed = _timeProvider.GetElapsedTime(startedAt);
        LogInitialScanCompleted(_logger, totalProcessed, elapsed.TotalMilliseconds);
    }

    private async Task<int> SyncRootAsync(string root, CancellationToken cancellationToken)
    {
        // Aufgespalten in Scan+Persist (Hot-Path mit Batch-Saves und Scope-Rotation)
        // und Tombstone-Cleanup (separater Final-Scope).
        (HashSet<string> filesOnDisk, int totalProcessed) =
            await ScanAndPersistAsync(root, cancellationToken).ConfigureAwait(false);
        await RemoveTombstonedFilesAsync(root, filesOnDisk, cancellationToken).ConfigureAwait(false);
        RaiseProgress(root, totalProcessed, isCompleted: true);
        return filesOnDisk.Count;
    }

    private async Task<(HashSet<string> FilesOnDisk, int TotalProcessed)> ScanAndPersistAsync(
        string root,
        CancellationToken cancellationToken)
    {
        // Batching im Initial-Scan: nach jeweils _options.InitialScanBatchSize Dateien
        // committen wir Zwischen-Stand und feuern ein Progress-Event. Auf grossen Roots
        // (mehrere Tausend .md-Dateien) wird damit der "Alle Dateien"-Tab inkrementell
        // befuellt — er bleibt sonst minutenlang leer, bis SaveChangesAsync ganz am Ende greift.
        int batchSize = Math.Max(1, _options.InitialScanBatchSize);
        HashSet<string> filesOnDisk = new(PathComparer);
        int totalProcessed = 0;
        int batchAccumulator = 0;
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        try
        {
            IMarkdownFileRepository repository = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();

            foreach (string path in _scanner.EnumerateMarkdownFiles(root, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = filesOnDisk.Add(path);
                await UpsertAsync(repository, path, root, cancellationToken).ConfigureAwait(false);
                totalProcessed++;
                batchAccumulator++;

                if (batchAccumulator >= batchSize)
                {
                    _ = await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    LogBatchCommitted(_logger, batchAccumulator, totalProcessed, root);
                    RaiseProgress(root, totalProcessed, isCompleted: false);

                    // Neuer Scope: EF-Change-Tracker bleibt schlank — bei 10k+ Files
                    // wuerde ein einziger Tracker ueberproportional viel RAM ziehen.
                    await scope.DisposeAsync().ConfigureAwait(false);
                    scope = _scopeFactory.CreateAsyncScope();
                    repository = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();
                    batchAccumulator = 0;
                }
            }

            if (batchAccumulator > 0)
            {
                _ = await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                LogBatchCommitted(_logger, batchAccumulator, totalProcessed, root);
            }
            return (filesOnDisk, totalProcessed);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task RemoveTombstonedFilesAsync(
        string root,
        HashSet<string> filesOnDisk,
        CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            IMarkdownFileRepository repository = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();
            IReadOnlyList<MarkdownFile> stored = await repository
                .GetAllUnderRootAsync(root, cancellationToken)
                .ConfigureAwait(false);

            bool anyRemoved = false;
            foreach (MarkdownFile entry in stored.Where(entry => !filesOnDisk.Contains(entry.AbsolutePath)))
            {
                repository.Remove(entry);
                LogFileRemoved(_logger, entry.AbsolutePath);
                anyRemoved = true;
            }
            if (anyRemoved)
            {
                _ = await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void RaiseProgress(string root, int processedCount, bool isCompleted)
    {
        EventHandler<IndexerScanProgressEventArgs>? handler = InitialScanProgress;
        if (handler is null)
        {
            return;
        }
        try
        {
            handler(this, new IndexerScanProgressEventArgs(root, processedCount, isCompleted));
        }
        catch (Exception ex) when (ex is InvalidOperationException)
        {
            // UI-Marshalling-Fehler darf den Scan nicht abbrechen.
            LogProgressDispatchFailed(_logger, ex);
        }
    }

    /// <inheritdoc />
    public event EventHandler<IndexerScanProgressEventArgs>? InitialScanProgress;

    private async Task ConsumeEventsAsync(ChannelReader<FileSystemEvent> reader, CancellationToken cancellationToken)
    {
        List<FileSystemEvent> batch = new(_options.BatchSize);
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (reader.TryRead(out FileSystemEvent? fileSystemEvent))
            {
                batch.Add(fileSystemEvent);
                if (batch.Count >= _options.BatchSize)
                {
                    await ProcessBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                    batch.Clear();
                }
            }
            if (batch.Count > 0)
            {
                await ProcessBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                batch.Clear();
            }
        }
    }

    private async Task ProcessBatchAsync(IReadOnlyList<FileSystemEvent> batch, CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            IMarkdownFileRepository repository = scope.ServiceProvider.GetRequiredService<IMarkdownFileRepository>();

            try
            {
                foreach (FileSystemEvent fileSystemEvent in batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ProcessEventAsync(repository, fileSystemEvent, cancellationToken).ConfigureAwait(false);
                }

                _ = await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is DbException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                // Eine Exception in einem Batch (SQLite-Spitze, IO-Fehler, kaputter
                // FileSystemEvent) darf die ConsumeEventsAsync-Schleife nicht abbrechen — naechster
                // Read laeuft weiter, betroffener Batch verloren.
                LogProcessBatchFailed(_logger, ex);
            }
        }
    }

    private async Task ProcessEventAsync(IMarkdownFileRepository repository, FileSystemEvent fileSystemEvent, CancellationToken cancellationToken)
    {
        switch (fileSystemEvent.Kind)
        {
            case FileSystemEventKind.Created:
            case FileSystemEventKind.Changed:
                await UpsertAsync(repository, fileSystemEvent.Path, fileSystemEvent.Root, cancellationToken).ConfigureAwait(false);
                break;
            case FileSystemEventKind.Renamed:
                await ProcessRenameAsync(repository, fileSystemEvent, cancellationToken).ConfigureAwait(false);
                break;
            case FileSystemEventKind.Deleted:
                await ProcessDeleteAsync(repository, fileSystemEvent.Path, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException($"Unbekannter Ereignistyp: {fileSystemEvent.Kind}.");
        }
    }

    private async Task UpsertAsync(IMarkdownFileRepository repository, string path, string root, CancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(path))
        {
            return;
        }

        long sizeBytes = _fileSystem.GetFileSize(path);
        DateTime lastWriteUtc = _fileSystem.GetLastWriteTimeUtc(path);
        MarkdownFile? existing = await repository.GetByAbsolutePathAsync(path, cancellationToken).ConfigureAwait(false);

        if (existing is not null && existing.SizeBytes == sizeBytes && existing.LastWriteTimeUtc == lastWriteUtc)
        {
            return;
        }

        string? hash = await ComputeHashWithRetryAsync(path, cancellationToken).ConfigureAwait(false);
        if (hash is null)
        {
            return;
        }

        if (existing is null)
        {
            await AddNewAsync(repository, path, root, sizeBytes, lastWriteUtc, hash, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (existing.ContentHash == hash)
        {
            TouchExisting(repository, existing, path, sizeBytes, lastWriteUtc);
            return;
        }
        UpdateExisting(repository, existing, path, sizeBytes, lastWriteUtc, hash);
    }

    private async Task AddNewAsync(
        IMarkdownFileRepository repository,
        string path,
        string root,
        long sizeBytes,
        DateTime lastWriteUtc,
        string hash,
        CancellationToken cancellationToken)
    {
        MarkdownFile created = new()
        {
            Id = Guid.NewGuid(),
            AbsolutePath = path,
            RelativePath = Path.GetRelativePath(root, path),
            FileNameWithoutExtension = Path.GetFileNameWithoutExtension(path),
            SizeBytes = sizeBytes,
            LastWriteTimeUtc = lastWriteUtc,
            ContentHash = hash,
            IndexedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
        };
        await repository.AddAsync(created, cancellationToken).ConfigureAwait(false);
        LogFileAdded(_logger, path);
    }

    private void TouchExisting(
        IMarkdownFileRepository repository,
        MarkdownFile existing,
        string path,
        long sizeBytes,
        DateTime lastWriteUtc)
    {
        existing.SizeBytes = sizeBytes;
        existing.LastWriteTimeUtc = lastWriteUtc;
        repository.Update(existing);
        LogFileTouched(_logger, path);
    }

    private void UpdateExisting(
        IMarkdownFileRepository repository,
        MarkdownFile existing,
        string path,
        long sizeBytes,
        DateTime lastWriteUtc,
        string hash)
    {
        existing.SizeBytes = sizeBytes;
        existing.LastWriteTimeUtc = lastWriteUtc;
        existing.ContentHash = hash;
        existing.IndexedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        repository.Update(existing);
        LogFileUpdated(_logger, path);
    }

    private async Task ProcessRenameAsync(IMarkdownFileRepository repository, FileSystemEvent fileSystemEvent, CancellationToken cancellationToken)
    {
        string? oldPath = fileSystemEvent.OldPath;
        if (string.IsNullOrWhiteSpace(oldPath))
        {
            await UpsertAsync(repository, fileSystemEvent.Path, fileSystemEvent.Root, cancellationToken).ConfigureAwait(false);
            return;
        }

        MarkdownFile? existing = await repository.GetByAbsolutePathAsync(oldPath, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            await UpsertAsync(repository, fileSystemEvent.Path, fileSystemEvent.Root, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!_fileSystem.FileExists(fileSystemEvent.Path))
        {
            repository.Remove(existing);
            LogFileRemoved(_logger, oldPath);
            return;
        }

        existing.AbsolutePath = fileSystemEvent.Path;
        existing.RelativePath = Path.GetRelativePath(fileSystemEvent.Root, fileSystemEvent.Path);
        existing.FileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileSystemEvent.Path);
        existing.SizeBytes = _fileSystem.GetFileSize(fileSystemEvent.Path);
        existing.LastWriteTimeUtc = _fileSystem.GetLastWriteTimeUtc(fileSystemEvent.Path);
        existing.IndexedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        repository.Update(existing);
        LogFileRenamed(_logger, oldPath, fileSystemEvent.Path);
    }

    private async Task ProcessDeleteAsync(IMarkdownFileRepository repository, string path, CancellationToken cancellationToken)
    {
        MarkdownFile? existing = await repository.GetByAbsolutePathAsync(path, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }
        repository.Remove(existing);
        LogFileRemoved(_logger, path);
    }

    private async Task<string?> ComputeHashWithRetryAsync(string path, CancellationToken cancellationToken)
    {
        const int MaxAttempts = 3;
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await _hashCalculator.ComputeAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == MaxAttempts - 1)
                {
                    LogHashFailed(_logger, path, ex);
                    return null;
                }
                TimeSpan delay = TimeSpan.FromMilliseconds(50L << attempt);
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        return null;
    }

    private async Task RunResyncLoopAsync(IReadOnlyList<string> roots, CancellationToken cancellationToken)
    {
        TimeSpan resyncInterval = TimeSpan.FromSeconds(_settings.Current.Behavior.IndexerResyncIntervalSeconds);
        if (resyncInterval <= TimeSpan.Zero)
        {
            return;
        }

        using PeriodicTimer timer = new(resyncInterval, _timeProvider);
        while (!cancellationToken.IsCancellationRequested)
        {
            bool elapsed;
            try
            {
                elapsed = await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (!elapsed)
            {
                return;
            }

            foreach (string root in roots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = await SyncRootAsync(root, cancellationToken).ConfigureAwait(false);
            }
            LogResyncCompleted(_logger);
        }
    }

    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "Indexer gestartet — {RootCount} Wurzel(n).")]
    private static partial void LogIndexerStarted(ILogger logger, int rootCount);

    [LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "Indexer gestoppt.")]
    private static partial void LogIndexerStopped(ILogger logger);

    [LoggerMessage(EventId = 102, Level = LogLevel.Warning, Message = "Keine gültigen Indexer-Wurzeln konfiguriert — Indexer beendet sich.")]
    private static partial void LogNoRoots(ILogger logger);

    [LoggerMessage(EventId = 103, Level = LogLevel.Warning, Message = "Konfigurierte Wurzel existiert nicht: {Root}")]
    private static partial void LogRootMissing(ILogger logger, string root);

    [LoggerMessage(EventId = 104, Level = LogLevel.Information, Message = "Initialer Scan abgeschlossen — {Count} Datei(en) in {ElapsedMs} ms.")]
    private static partial void LogInitialScanCompleted(ILogger logger, int count, double elapsedMs);

    [LoggerMessage(EventId = 105, Level = LogLevel.Debug, Message = "Datei indiziert: {Path}")]
    private static partial void LogFileAdded(ILogger logger, string path);

    [LoggerMessage(EventId = 106, Level = LogLevel.Debug, Message = "Datei aktualisiert: {Path}")]
    private static partial void LogFileUpdated(ILogger logger, string path);

    [LoggerMessage(EventId = 107, Level = LogLevel.Debug, Message = "Datei berührt (Inhalt unverändert): {Path}")]
    private static partial void LogFileTouched(ILogger logger, string path);

    [LoggerMessage(EventId = 108, Level = LogLevel.Debug, Message = "Datei umbenannt: {OldPath} -> {NewPath}")]
    private static partial void LogFileRenamed(ILogger logger, string oldPath, string newPath);

    [LoggerMessage(EventId = 109, Level = LogLevel.Debug, Message = "Datei entfernt: {Path}")]
    private static partial void LogFileRemoved(ILogger logger, string path);

    [LoggerMessage(EventId = 110, Level = LogLevel.Warning, Message = "Hash-Berechnung endgültig fehlgeschlagen für {Path}.")]
    private static partial void LogHashFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 111, Level = LogLevel.Debug, Message = "Periodischer Re-Sync abgeschlossen.")]
    private static partial void LogResyncCompleted(ILogger logger);

    [LoggerMessage(EventId = 112, Level = LogLevel.Information, Message = "Batch persistiert ({BatchCount} Dateien, gesamt {TotalCount}) für Wurzel {Root}.")]
    private static partial void LogBatchCommitted(ILogger logger, int batchCount, int totalCount, string root);

    [LoggerMessage(EventId = 113, Level = LogLevel.Warning, Message = "Indexer-Fortschritts-Event konnte nicht zugestellt werden.")]
    private static partial void LogProgressDispatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 114, Level = LogLevel.Warning, Message = "Indexer-Batch fehlgeschlagen — Schleife laeuft weiter.")]
    private static partial void LogProcessBatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 115, Level = LogLevel.Error, Message = "MarkdownIndexer-Watchdog: unerwartete Exception aufgefangen, Service wird ordentlich beendet.")]
    private static partial void LogWatchdogTriggered(ILogger logger, Exception exception);
}
