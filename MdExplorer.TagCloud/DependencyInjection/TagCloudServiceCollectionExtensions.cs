using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace MdExplorer.TagCloud.DependencyInjection;

/// <summary>DI-Registrierung des Tag-Cloud-Moduls.</summary>
public static class TagCloudServiceCollectionExtensions
{
    /// <summary>
    /// Registriert <see cref="ITagStatisticsService"/> und den Hintergrund-Refresh-Service.
    /// Der Caller muss <c>AddData()</c> bereits registriert haben, damit der
    /// <c>MdExplorerDbContext</c> über einen Scope auflösbar ist. Der
    /// <see cref="IMessenger"/> wird einmalig als Singleton angelegt — bereits registrierte
    /// Implementierungen werden respektiert.
    /// </summary>
    public static IServiceCollection AddTagCloud(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        _ = services.AddScoped<ITagStatisticsService, TagStatisticsService>();
        _ = services.AddScoped<ITagManagementService, TagManagementService>();
        _ = services.AddSingleton<TagCloudRefreshService>();
        _ = services.AddHostedService(sp => sp.GetRequiredService<TagCloudRefreshService>());

        return services;
    }
}
