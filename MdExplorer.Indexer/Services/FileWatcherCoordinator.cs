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

        foreach (string root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }
            IFileWatcher watcher = _factory.Create(root);
            watcher.EventOccurred += OnRawEvent;
            watcher.Start();
            _watchers.Add(watcher);
        }

        _started = true;
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

            if (_pending.TryGetValue(rawEvent.Path, out PendingEvent? existing))
            {
                existing.Latest = rawEvent;
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

    [LoggerMessage(EventId = 50, Level = LogLevel.Information,
        Message = "FileWatcher gestartet — {WatcherCount} Wurzel(n), Debounce {DebounceMs} ms.")]
    private static partial void LogStarted(ILogger logger, int watcherCount, double debounceMs);

    [LoggerMessage(EventId = 51, Level = LogLevel.Information, Message = "FileWatcher gestoppt.")]
    private static partial void LogStopped(ILogger logger);

    private sealed class PendingEvent(FileSystemEvent latest, ITimer timer)
    {
        public FileSystemEvent Latest { get; set; } = latest;
        public ITimer Timer { get; } = timer;
    }
}
