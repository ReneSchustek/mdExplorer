using System.Diagnostics.CodeAnalysis;
using MdExplorer.Core.Abstractions;
using MdExplorer.Graph.Abstractions;
using MdExplorer.Graph.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.Graph.DependencyInjection;

/// <summary>DI-Registrierung des Graph-Moduls.</summary>
[ExcludeFromCodeCoverage]
public static class GraphServiceCollectionExtensions
{
    /// <summary>
    /// Registriert <see cref="IGraphService"/>. <see cref="GraphJsonBuilder"/> ist eine
    /// statische Utility-Klasse und benötigt keine DI-Registrierung. Erwartet, dass
    /// <see cref="IGraphSourceProvider"/> bereits aus der Data-Schicht registriert ist und
    /// ein <c>ITagNormalizer</c> aus dem Parser-Modul aufgelöst werden kann.
    /// </summary>
    /// <param name="services">Service-Sammlung, in die das Graph-Modul registriert wird.</param>
    /// <returns>Die übergebene <paramref name="services"/>-Instanz zur Methoden-Verkettung.</returns>
    public static IServiceCollection AddGraph(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _ = services.AddScoped<IGraphService, GraphService>();

        return services;
    }
}
