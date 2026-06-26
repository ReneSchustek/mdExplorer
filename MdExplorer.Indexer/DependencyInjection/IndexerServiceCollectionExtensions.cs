using MdExplorer.Core.Settings;
using MdExplorer.Indexer.Abstractions;
using MdExplorer.Indexer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MdExplorer.Indexer.DependencyInjection;

/// <summary>DI-Registrierung des Indexer-Moduls.</summary>
public static class IndexerServiceCollectionExtensions
{
    /// <summary>
    /// Registriert <see cref="MarkdownIndexer"/> als <see cref="IHostedService"/> sowie alle
    /// modulinternen Abhängigkeiten. Erwartet, dass <c>IndexerOptions</c> bereits über das Optionssystem
    /// registriert wurde, ein <c>IMarkdownFileRepository</c> aus der Data-Schicht vorhanden ist und
    /// <c>ISettingsService</c> aus der Core-Schicht aufgelöst werden kann (für Roots und Excludes).
    /// </summary>
    public static IServiceCollection AddIndexer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _ = services.AddSingleton<MdIgnoreReader>();
        _ = services.AddSingleton<MdIgnoreHierarchy>();
        _ = services.AddSingleton<GlobExclusionFilter>();
        _ = services.AddSingleton<IExclusionFilter>(sp => sp.GetRequiredService<GlobExclusionFilter>());
        _ = services.AddSingleton<IFileScanner, FileScanner>();
        _ = services.AddSingleton<IHashCalculator, HashCalculator>();
        _ = services.AddSingleton<IFileWatcherFactory, LocalFileWatcherFactory>();
        _ = services.AddSingleton<FileWatcherCoordinator>();
        _ = services.AddSingleton<MarkdownIndexer>();
        _ = services.AddSingleton<IIndexer>(sp => sp.GetRequiredService<MarkdownIndexer>());
        _ = services.AddHostedService(sp => sp.GetRequiredService<MarkdownIndexer>());

        return services;
    }
}
