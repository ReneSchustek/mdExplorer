using MdExplorer.Indexer.Models;

namespace MdExplorer.Indexer.Abstractions;

/// <summary>
/// Abstrakter Datei-System-Watcher. Wird vom <c>FileWatcherCoordinator</c>
/// genutzt, um <see cref="FileSystemWatcher"/> aus der Konkretion herauszuhalten und Tests deterministisch zu halten.
/// </summary>
public interface IFileWatcher : IDisposable
{
    /// <summary>Wird ausgelöst, sobald die zugrunde liegende Quelle ein Roh-Ereignis liefert.</summary>
    event EventHandler<WatcherEventArgs>? EventOccurred;

    /// <summary>Startet die Beobachtung.</summary>
    void Start();
}
