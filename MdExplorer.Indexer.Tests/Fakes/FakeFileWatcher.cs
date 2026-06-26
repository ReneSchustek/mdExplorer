using MdExplorer.Indexer.Abstractions;
using MdExplorer.Indexer.Models;

namespace MdExplorer.Indexer.Tests.Fakes;

/// <summary>
/// Test-Watcher, der Ereignisse manuell aus dem Test getriggert werden — vollständig deterministisch.
/// </summary>
internal sealed class FakeFileWatcher : IFileWatcher
{
    public bool IsStarted { get; private set; }

    public bool IsDisposed { get; private set; }

    public event EventHandler<WatcherEventArgs>? EventOccurred;

    public void Start() => IsStarted = true;

    public void Dispose() => IsDisposed = true;

    public void TriggerEvent(FileSystemEvent fileSystemEvent)
    {
        ArgumentNullException.ThrowIfNull(fileSystemEvent);
        EventOccurred?.Invoke(this, new WatcherEventArgs(fileSystemEvent));
    }
}

/// <summary>Test-Fabrik mit Index-basierter Zuordnung der erzeugten Watcher.</summary>
internal sealed class FakeFileWatcherFactory : IFileWatcherFactory
{
    private readonly Dictionary<string, FakeFileWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, FakeFileWatcher> Watchers => _watchers;

    public IFileWatcher Create(string rootAbsolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootAbsolutePath);
        FakeFileWatcher watcher = new();
        _watchers[rootAbsolutePath] = watcher;
        return watcher;
    }
}
