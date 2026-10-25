using System.Diagnostics.CodeAnalysis;
using MdExplorer.Indexer.Abstractions;
using MdExplorer.Indexer.Models;

namespace MdExplorer.Indexer.Services;

/// <summary>
/// Produktiver <see cref="IFileWatcher"/>: drei <see cref="FileSystemWatcher"/> je Wurzel, deren
/// Roh-Ereignisse in <see cref="FileSystemEvent"/> übersetzt werden.
/// </summary>
/// <remarks>
/// <para>
/// Drei statt einem, weil der Namensfilter eines <see cref="FileSystemWatcher"/> auch auf Ordner
/// wirkt: Ein Watcher mit <c>*.md</c> meldet nie, dass ein Ordner umbenannt oder gelöscht wurde,
/// und für die Dateien darin kommt dabei kein Ereignis. Ein zweiter Watcher ohne Namensfilter
/// beobachtet deshalb nur Ordnernamen, ein dritter die <c>.mdignore</c>-Dateien.
/// </para>
/// <para>
/// Läuft der Puffer eines Watchers über, sind Ereignisse verloren. Gemeldet wird dann ein
/// vollständiger Abgleich der Wurzel, statt auf den nächsten Takt zu warten; ist der Takt
/// abgeschaltet, käme sonst nie einer.
/// </para>
/// </remarks>
[ExcludeFromCodeCoverage]
internal sealed class LocalFileWatcher : IFileWatcher
{
    private const string MarkdownFilter = "*.md";
    private const string IgnoreFileName = ".mdignore";

    // Der Standardpuffer von 8 KB läuft bei vielen Änderungen auf einmal über, und dann gehen Ereignisse
    // verloren. 64 KB ist die Obergrenze, die FileSystemWatcher.InternalBufferSize laut Dokumentation erlaubt.
    private const int WatcherBufferSizeBytes = 64 * 1024;

    private readonly string _root;
    private readonly FileSystemWatcher _files;
    private readonly FileSystemWatcher _directories;
    private readonly FileSystemWatcher _ignoreFiles;
    private bool _disposed;

    public LocalFileWatcher(string rootAbsolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootAbsolutePath);
        _root = rootAbsolutePath;

        _files = Create(MarkdownFilter, NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size);
        _files.Created += OnFileCreated;
        _files.Changed += OnFileChanged;
        _files.Renamed += OnFileRenamed;
        _files.Deleted += OnFileDeleted;

        _directories = Create(string.Empty, NotifyFilters.DirectoryName);
        _directories.Created += OnDirectoryCreated;
        _directories.Renamed += OnDirectoryRenamed;
        _directories.Deleted += OnDirectoryDeleted;

        _ignoreFiles = Create(IgnoreFileName, NotifyFilters.FileName | NotifyFilters.LastWrite);
        _ignoreFiles.Created += OnIgnoreFileChanged;
        _ignoreFiles.Changed += OnIgnoreFileChanged;
        _ignoreFiles.Renamed += OnIgnoreFileChanged;
        _ignoreFiles.Deleted += OnIgnoreFileChanged;

        foreach (FileSystemWatcher watcher in Watchers)
        {
            watcher.Error += OnError;
        }
    }

    public event EventHandler<WatcherEventArgs>? EventOccurred;

    private IEnumerable<FileSystemWatcher> Watchers => [_files, _directories, _ignoreFiles];

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (FileSystemWatcher watcher in Watchers)
        {
            watcher.EnableRaisingEvents = true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _files.Dispose();
        _directories.Dispose();
        _ignoreFiles.Dispose();
        _disposed = true;
    }

    private FileSystemWatcher Create(string filter, NotifyFilters notifyFilters) => new(_root, filter)
    {
        IncludeSubdirectories = true,
        NotifyFilter = notifyFilters,
        InternalBufferSize = WatcherBufferSizeBytes,
    };

    private void OnFileCreated(object sender, FileSystemEventArgs args) => Raise(FileSystemEventKind.Created, args.FullPath);

    private void OnFileChanged(object sender, FileSystemEventArgs args) => Raise(FileSystemEventKind.Changed, args.FullPath);

    private void OnFileRenamed(object sender, RenamedEventArgs args) => Raise(FileSystemEventKind.Renamed, args.FullPath, args.OldFullPath);

    private void OnFileDeleted(object sender, FileSystemEventArgs args) => Raise(FileSystemEventKind.Deleted, args.FullPath);

    // Ein neu angelegter oder hereinverschobener Ordner kann schon Dateien enthalten, die der Watcher
    // nicht einzeln meldet. Der Abgleich der Wurzel findet sie.
    private void OnDirectoryCreated(object sender, FileSystemEventArgs args) => Raise(FileSystemEventKind.ResyncRequested, args.FullPath);

    private void OnDirectoryRenamed(object sender, RenamedEventArgs args) => Raise(FileSystemEventKind.DirectoryRenamed, args.FullPath, args.OldFullPath);

    private void OnDirectoryDeleted(object sender, FileSystemEventArgs args) => Raise(FileSystemEventKind.DirectoryDeleted, args.FullPath);

    private void OnIgnoreFileChanged(object sender, FileSystemEventArgs args) => Raise(FileSystemEventKind.IgnoreFileChanged, args.FullPath);

    private void OnError(object sender, ErrorEventArgs args) => Raise(FileSystemEventKind.ResyncRequested, _root);

    private void Raise(FileSystemEventKind kind, string path, string? oldPath = null) =>
        EventOccurred?.Invoke(this, new WatcherEventArgs(new FileSystemEvent(kind, path, oldPath, _root)));
}
