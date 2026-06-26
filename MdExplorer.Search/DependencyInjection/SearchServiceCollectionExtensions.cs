using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MdExplorer.Search.DependencyInjection;

/// <summary>DI-Registrierung des Such-Moduls.</summary>
public static class SearchServiceCollectionExtensions
{
    /// <summary>
    /// Registriert <see cref="ISearchService"/>, <see cref="ISearchQueryBuilder"/> sowie
    /// den <see cref="Fts5IndexMaintainer"/> als <see cref="IHostedService"/>. Erwartet, dass
    /// <c>SearchOptions</c> bereits über das Optionssystem gebunden ist und die Data-Schicht
    /// (<c>AddData</c>) den <c>MdExplorerDbContext</c> bereitstellt.
    /// </summary>
    public static IServiceCollection AddSearch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _ = services.AddSingleton<ISynonymProvider, FileSynonymProvider>();
        _ = services.AddSingleton<ISearchQueryBuilder, SearchQueryBuilder>();
        _ = services.AddSingleton<ISimilarityQueryBuilder, SimilarityQueryBuilder>();
        _ = services.AddScoped<ISearchService, Fts5SearchService>();
        _ = services.AddSingleton<Fts5IndexMaintainer>();
        _ = services.AddSingleton<ISearchIndexer>(sp => sp.GetRequiredService<Fts5IndexMaintainer>());
        _ = services.AddHostedService(sp => sp.GetRequiredService<Fts5IndexMaintainer>());

        return services;
    }
}
