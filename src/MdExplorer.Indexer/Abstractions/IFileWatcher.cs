using MdExplorer.Indexer.Models;

namespace MdExplorer.Indexer.Abstractions;

/// <summary>
/// Quelle roher Dateisystem-Ereignisse für den <c>FileWatcherCoordinator</c>. Die Schnittstelle hält
/// <see cref="FileSystemWatcher"/> aus dem Koordinator heraus, damit Tests Ereignisse gezielt einspeisen können.
/// </summary>
public interface IFileWatcher : IDisposable
{
    /// <summary>Wird ausgelöst, sobald die zugrunde liegende Quelle ein Roh-Ereignis liefert.</summary>
    event EventHandler<WatcherEventArgs>? EventOccurred;

    /// <summary>Startet die Beobachtung.</summary>
    void Start();
}
