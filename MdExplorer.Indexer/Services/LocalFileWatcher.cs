using MdExplorer.Indexer.Abstractions;
using MdExplorer.Indexer.Models;

namespace MdExplorer.Indexer.Services;

/// <summary>
/// Produktiver <see cref="IFileWatcher"/>, der einen <see cref="FileSystemWatcher"/>
/// kapselt und dessen Roh-Ereignisse in <see cref="FileSystemEvent"/> übersetzt.
/// </summary>
internal sealed class LocalFileWatcher : IFileWatcher
{
    private const string MarkdownFilter = "*.md";
    private const NotifyFilters WatchedFilters =
        NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName;

    private readonly string _root;
    private readonly FileSystemWatcher _watcher;
    private bool _disposed;

    public LocalFileWatcher(string rootAbsolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootAbsolutePath);
        _root = rootAbsolutePath;

        _watcher = new FileSystemWatcher(rootAbsolutePath, MarkdownFilter)
        {
            IncludeSubdirectories = true,
            NotifyFilter = WatchedFilters,
            InternalBufferSize = 64 * 1024,
        };

        _watcher.Created += OnCreated;
        _watcher.Changed += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Deleted += OnDeleted;
    }

    public event EventHandler<WatcherEventArgs>? EventOccurred;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _watcher.EnableRaisingEvents = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _watcher.Created -= OnCreated;
        _watcher.Changed -= OnChanged;
        _watcher.Renamed -= OnRenamed;
        _watcher.Deleted -= OnDeleted;
        _watcher.Dispose();
        _disposed = true;
    }

    private void OnCreated(object sender, FileSystemEventArgs args) =>
        Raise(new FileSystemEvent(FileSystemEventKind.Created, args.FullPath, OldPath: null, _root));

    private void OnChanged(object sender, FileSystemEventArgs args) =>
        Raise(new FileSystemEvent(FileSystemEventKind.Changed, args.FullPath, OldPath: null, _root));

    private void OnRenamed(object sender, RenamedEventArgs args) =>
        Raise(new FileSystemEvent(FileSystemEventKind.Renamed, args.FullPath, args.OldFullPath, _root));

    private void OnDeleted(object sender, FileSystemEventArgs args) =>
        Raise(new FileSystemEvent(FileSystemEventKind.Deleted, args.FullPath, OldPath: null, _root));

    private void Raise(FileSystemEvent fileSystemEvent) =>
        EventOccurred?.Invoke(this, new WatcherEventArgs(fileSystemEvent));
}
