namespace MdExplorer.Indexer.Models;

/// <summary>Ereignisarten, die der Indexer aus dem Dateisystem konsumiert.</summary>
public enum FileSystemEventKind
{
    /// <summary>Eine neue Datei wurde angelegt.</summary>
    Created,

    /// <summary>Eine vorhandene Datei wurde verändert.</summary>
    Changed,

    /// <summary>Eine vorhandene Datei wurde umbenannt.</summary>
    Renamed,

    /// <summary>Eine vorhandene Datei wurde gelöscht.</summary>
    Deleted,
}

/// <summary>
/// Debounciertes Datei-System-Ereignis, das zwischen <c>FileWatcherCoordinator</c>
/// und <c>MarkdownIndexer</c> über einen <see cref="System.Threading.Channels.Channel{T}"/> fließt.
/// </summary>
/// <param name="Kind">Ereignisart.</param>
/// <param name="Path">Aktueller Pfad der Datei.</param>
/// <param name="OldPath">Bei <see cref="FileSystemEventKind.Renamed"/> der vorherige Pfad — sonst <see langword="null"/>.</param>
/// <param name="Root">Index-Wurzel, unter der das Ereignis aufgetreten ist.</param>
public sealed record FileSystemEvent(FileSystemEventKind Kind, string Path, string? OldPath, string Root);

/// <summary>
/// <see cref="EventArgs"/>-Hülle für <see cref="FileSystemEvent"/>. Wird vom <see cref="MdExplorer.Indexer.Abstractions.IFileWatcher"/>
/// genutzt, weil <see cref="EventHandler{T}"/> ein <see cref="EventArgs"/>-Derivat erwartet (CA1003) und Records nicht von <see cref="EventArgs"/> erben dürfen.
/// </summary>
public sealed class WatcherEventArgs : EventArgs
{
    /// <summary>Erzeugt eine neue Hülle um das gelieferte Ereignis.</summary>
    public WatcherEventArgs(FileSystemEvent fileSystemEvent)
    {
        ArgumentNullException.ThrowIfNull(fileSystemEvent);
        Event = fileSystemEvent;
    }

    /// <summary>Das gelieferte, debouncierungsfähige Datei-System-Ereignis.</summary>
    public FileSystemEvent Event { get; }
}
