using MdExplorer.Indexer.Abstractions;
using MdExplorer.Indexer.Models;

namespace MdExplorer.Indexer.Tests.Fakes;

/// <summary>
/// Watcher, dessen Ereignisse der Test von Hand auslöst. Damit hängt kein Test am echten
/// Dateisystem oder an dessen Zeitverhalten.
/// </summary>
internal sealed class FakeFileWatcher : IFileWatcher
{
    private EventHandler<WatcherEventArgs>? _lastSubscriber;

    public bool IsStarted { get; private set; }

    public bool IsDisposed { get; private set; }

    /// <summary>Wirft beim Start, wie ein Watcher auf einem abgezogenen Netzlaufwerk.</summary>
    public Exception? ThrowOnStart { get; set; }

    public event EventHandler<WatcherEventArgs>? EventOccurred
    {
        add
        {
            _lastSubscriber = value;
            Subscribers += value;
        }
        remove => Subscribers -= value;
    }

    private event EventHandler<WatcherEventArgs>? Subscribers;

    public void Start()
    {
        if (ThrowOnStart is not null)
        {
            throw ThrowOnStart;
        }
        IsStarted = true;
    }

    public void Dispose() => IsDisposed = true;

    public void TriggerEvent(FileSystemEvent fileSystemEvent)
    {
        ArgumentNullException.ThrowIfNull(fileSystemEvent);
        Subscribers?.Invoke(this, new WatcherEventArgs(fileSystemEvent));
    }

    /// <summary>
    /// Ein Ereignis, das der echte Watcher schon auf einem Pool-Thread zustellt, während der
    /// Koordinator sich gerade abmeldet: Es erreicht den Empfänger trotz Abmeldung.
    /// </summary>
    public void TriggerEventInFlight(FileSystemEvent fileSystemEvent)
    {
        ArgumentNullException.ThrowIfNull(fileSystemEvent);
        _lastSubscriber?.Invoke(this, new WatcherEventArgs(fileSystemEvent));
    }
}

/// <summary>
/// Fabrik, die jeden erzeugten Watcher unter seinem Wurzelpfad ablegt, damit der Test gezielt die
/// Ereignisse einer Wurzel auslösen kann.
/// </summary>
internal sealed class FakeFileWatcherFactory : IFileWatcherFactory
{
    private readonly Dictionary<string, FakeFileWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, FakeFileWatcher> Watchers => _watchers;

    /// <summary>Wurzeln, deren Watcher beim Start werfen sollen.</summary>
    public HashSet<string> FailingRoots { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IFileWatcher Create(string rootAbsolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootAbsolutePath);
        FakeFileWatcher watcher = new();
        if (FailingRoots.Contains(rootAbsolutePath))
        {
            watcher.ThrowOnStart = new IOException("Netzlaufwerk nicht erreichbar.");
        }
        _watchers[rootAbsolutePath] = watcher;
        return watcher;
    }
}
