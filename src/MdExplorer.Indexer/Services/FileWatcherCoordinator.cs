using System.Threading.Channels;
using MdExplorer.Indexer.Abstractions;
using MdExplorer.Indexer.Models;
using MdExplorer.Indexer.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MdExplorer.Indexer.Services;

/// <summary>
/// Sammelt rohe Watcher-Ereignisse, debounciert sie pro Pfad und stellt die stabilen
/// Ereignisse über einen <see cref="Channel{T}"/> für den Konsumenten bereit.
/// Thread-sicher: <see cref="FileSystemWatcher"/> liefert aus dem ThreadPool.
/// </summary>
public sealed partial class FileWatcherCoordinator : IAsyncDisposable
{
    private readonly IFileWatcherFactory _factory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FileWatcherCoordinator> _logger;
    private readonly TimeSpan _debounce;
    private readonly Channel<FileSystemEvent> _channel;
    private readonly object _gate = new();
    private readonly Dictionary<string, PendingEvent> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IFileWatcher> _watchers = [];
    private bool _started;
    private bool _stopped;
    private bool _disposed;

    /// <summary>Erzeugt den Coordinator anhand der konfigurierten Debounce-Zeit.</summary>
    public FileWatcherCoordinator(
        IFileWatcherFactory factory,
        IOptions<IndexerOptions> options,
        TimeProvider timeProvider,
        ILogger<FileWatcherCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _factory = factory;
        _timeProvider = timeProvider;
        _logger = logger;
        _debounce = TimeSpan.FromMilliseconds(options.Value.DebounceMs);

        // Unbegrenzt, weil die Entprellung je Pfad schon zusammenfasst: Ein Speichersturm auf eine
        // Datei landet als ein Eintrag im Kanal. Schreiber sind die Timer-Rückrufe, gelesen wird nur
        // vom Indexer.
        _channel = Channel.CreateUnbounded<FileSystemEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>Liefert die debouncierten Datei-System-Ereignisse als Reader.</summary>
    public ChannelReader<FileSystemEvent> Events => _channel.Reader;

    /// <summary>Startet die Watcher für die angegebenen Wurzeln.</summary>
    public void Start(IReadOnlyList<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("FileWatcherCoordinator wurde bereits gestartet.");
        }

        _started = true;
        foreach (string root in roots.Where(root => !string.IsNullOrWhiteSpace(root)))
        {
            StartWatcher(root);
        }

        LogStarted(_logger, _watchers.Count, _debounce.TotalMilliseconds);
    }

    /// <summary>Beendet die Beobachtung und schließt den Channel.</summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed || !_started)
        {
            return Task.CompletedTask;
        }

        foreach (IFileWatcher watcher in _watchers)
        {
            watcher.EventOccurred -= OnRawEvent;
            watcher.Dispose();
        }
        _watchers.Clear();

        // Was beim Anhalten noch in der Entprellung steckt, wird verworfen und nicht mehr gemeldet.
        PendingEvent[] outstanding;
        lock (_gate)
        {
            _stopped = true;
            outstanding = [.. _pending.Values];
            _pending.Clear();
        }
        foreach (PendingEvent entry in outstanding)
        {
            entry.Timer.Dispose();
        }

        _ = _channel.Writer.TryComplete();
        LogStopped(_logger);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _disposed = true;
    }

    [LoggerMessage(EventId = 50, Level = LogLevel.Information,
        Message = "FileWatcher gestartet — {WatcherCount} Wurzel(n), Debounce {DebounceMs} ms.")]
    private static partial void LogStarted(ILogger logger, int watcherCount, double debounceMs);

    [LoggerMessage(EventId = 51, Level = LogLevel.Information, Message = "FileWatcher gestoppt.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(EventId = 52, Level = LogLevel.Warning,
        Message = "FileWatcher für {Root} ließ sich nicht starten; Änderungen dort holt der Abgleich nach.")]
    private static partial void LogWatcherStartFailed(ILogger logger, Exception exception, string root);

    private static bool IsStructural(FileSystemEventKind kind) =>
        kind is FileSystemEventKind.DirectoryRenamed
            or FileSystemEventKind.DirectoryDeleted
            or FileSystemEventKind.ResyncRequested
            or FileSystemEventKind.IgnoreFileChanged;

    private static bool IsSecondRenameOntoTheSameTarget(FileSystemEvent pending, FileSystemEvent incoming) =>
        pending.Kind == FileSystemEventKind.Renamed
            && incoming.Kind == FileSystemEventKind.Renamed
            && !string.Equals(pending.OldPath, incoming.OldPath, StringComparison.OrdinalIgnoreCase);

    // Ein Netzlaufwerk, das gerade fehlt, oder ein Ordner ohne Leserecht kostet nur die eigene
    // Wurzel. Der kaputte Watcher wird sofort freigegeben, damit er keinen Griff offen hält.
    private void StartWatcher(string root)
    {
        IFileWatcher watcher = _factory.Create(root);
        try
        {
            watcher.EventOccurred += OnRawEvent;
            watcher.Start();
            _watchers.Add(watcher);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            watcher.EventOccurred -= OnRawEvent;
            watcher.Dispose();
            LogWatcherStartFailed(_logger, exception, root);
        }
    }

    private void OnRawEvent(object? sender, WatcherEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        FileSystemEvent rawEvent = args.Event;

        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            // Ordnerereignisse, Pufferüberlauf und eine geänderte .mdignore betreffen viele Dateien
            // auf einmal. Sie gehen ohne Wartezeit weiter, damit spätere Dateiereignisse im neuen
            // Ordner nicht vor ihnen beim Indexer ankommen.
            if (IsStructural(rawEvent.Kind))
            {
                _ = _channel.Writer.TryWrite(rawEvent);
                return;
            }

            rawEvent = FoldRenameChain(rawEvent);

            // Jedes weitere Ereignis am selben Pfad startet die Wartezeit neu, und das letzte gewinnt:
            // Angelegt und gleich wieder gelöscht kommt nur als Löschung beim Indexer an.
            // Ausnahme ist eine Umbenennung. Nur sie trägt den alten Pfad; ginge sie in einem
            // folgenden Changed auf, legte der Indexer die neue Datei an und der alte Eintrag
            // bliebe als Waise stehen. Inhaltsänderung und spätere Löschung erkennt der
            // Rename-Zweig des Indexers selbst.
            if (_pending.TryGetValue(rawEvent.Path, out PendingEvent? existing))
            {
                if (IsSecondRenameOntoTheSameTarget(existing.Latest, rawEvent))
                {
                    // Ginge die erste Umbenennung in der zweiten auf, verlöre sie ihren alten Pfad,
                    // und dessen Eintrag bliebe als Waise stehen. Sie geht deshalb sofort hinaus.
                    _ = _channel.Writer.TryWrite(existing.Latest);
                    existing.Latest = rawEvent;
                }
                else if (existing.Latest.Kind != FileSystemEventKind.Renamed || rawEvent.Kind == FileSystemEventKind.Renamed)
                {
                    existing.Latest = rawEvent;
                }
                _ = existing.Timer.Change(_debounce, Timeout.InfiniteTimeSpan);
                return;
            }

            ITimer timer = _timeProvider.CreateTimer(OnDebounceFired, rawEvent.Path, _debounce, Timeout.InfiniteTimeSpan);
            _pending[rawEvent.Path] = new PendingEvent(rawEvent, timer);
        }
    }

    private void OnDebounceFired(object? state)
    {
        if (state is not string key)
        {
            return;
        }

        PendingEvent? toFlush = null;
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }
            if (_pending.Remove(key, out PendingEvent? pending))
            {
                toFlush = pending;
            }
        }

        if (toFlush is null)
        {
            return;
        }

        toFlush.Timer.Dispose();
        _ = _channel.Writer.TryWrite(toFlush.Latest);
    }

    // a → b → c innerhalb der Entprellung wird zu a → c. Kämen beide Umbenennungen getrennt an,
    // könnte b → c vor a → b verarbeitet werden; der Eintrag von a ginge verloren und c bekäme
    // eine neue Id. Aufgerufen unter _gate.
    private FileSystemEvent FoldRenameChain(FileSystemEvent rawEvent)
    {
        if (rawEvent.Kind != FileSystemEventKind.Renamed
            || rawEvent.OldPath is null
            || !_pending.TryGetValue(rawEvent.OldPath, out PendingEvent? previous)
            || previous.Latest.Kind != FileSystemEventKind.Renamed)
        {
            return rawEvent;
        }

        _ = _pending.Remove(rawEvent.OldPath);
        previous.Timer.Dispose();
        return rawEvent with { OldPath = previous.Latest.OldPath };
    }

    /// <summary>
    /// Ein Pfad in der Entprellung: das zuletzt gemeldete Ereignis und der Timer, der es freigibt.
    /// </summary>
    private sealed class PendingEvent(FileSystemEvent latest, ITimer timer)
    {
        public FileSystemEvent Latest { get; set; } = latest;
        public ITimer Timer { get; } = timer;
    }
}
