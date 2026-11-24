namespace MdExplorer.Indexer.Models;

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
