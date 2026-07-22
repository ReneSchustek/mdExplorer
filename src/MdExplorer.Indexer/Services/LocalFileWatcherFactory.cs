using MdExplorer.Indexer.Abstractions;

using System.Diagnostics.CodeAnalysis;

namespace MdExplorer.Indexer.Services;

/// <summary>Produktive Fabrik, die <see cref="LocalFileWatcher"/>-Instanzen liefert.</summary>
[ExcludeFromCodeCoverage]
public sealed class LocalFileWatcherFactory : IFileWatcherFactory
{
    /// <inheritdoc />
    public IFileWatcher Create(string rootAbsolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootAbsolutePath);
        return new LocalFileWatcher(rootAbsolutePath);
    }
}
