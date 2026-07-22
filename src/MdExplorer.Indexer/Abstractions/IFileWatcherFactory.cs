namespace MdExplorer.Indexer.Abstractions;

/// <summary>
/// Fabrik für <see cref="IFileWatcher"/>-Instanzen.
/// Produktion: <c>LocalFileWatcherFactory</c>; Tests injizieren Fakes.
/// </summary>
public interface IFileWatcherFactory
{
    /// <summary>Erzeugt einen Watcher für die angegebene Wurzel.</summary>
    IFileWatcher Create(string rootAbsolutePath);
}
