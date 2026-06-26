using MdExplorer.Indexer.Abstractions;

namespace MdExplorer.Indexer.Services;

/// <summary>Produktive Fabrik, die <see cref="LocalFileWatcher"/>-Instanzen liefert.</summary>
public sealed class LocalFileWatcherFactory : IFileWatcherFactory
{
    /// <inheritdoc />
    public IFileWatcher Create(string rootAbsolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootAbsolutePath);
        return new LocalFileWatcher(rootAbsolutePath);
    }
}
