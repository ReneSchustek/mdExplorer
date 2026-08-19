using System.Diagnostics.CodeAnalysis;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Diagnostics;
using MdExplorer.Core.FileSystem;
using MdExplorer.Core.Settings;
using MdExplorer.Core.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Core.DependencyInjection;

/// <summary>
/// DI-Registrierung der Core-Schicht: Abstraktionen, Cross-Cutting-Services, Startup-Use-Cases.
/// </summary>
[ExcludeFromCodeCoverage]
public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Registriert <see cref="TimeProvider"/>, <see cref="IFileSystem"/>, den Betriebs-Stand der
    /// Parse-Fehlschläge, Settings-Service und Startup-Orchestrierung.
    /// </summary>
    public static IServiceCollection AddCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _ = services.AddSingleton(TimeProvider.System);
        _ = services.AddSingleton<IFileSystem, LocalFileSystem>();
        _ = services.AddSingleton<IParseFailureStatus, ParseFailureStatus>();
        _ = services.AddSingleton<ISettingsHistoryStore>(sp => new FileSystemSettingsHistoryStore(
            AppPaths.GetSettingsHistoryDirectory(),
            AppPaths.GetSettingsAuditLogPath(),
            FileSystemSettingsHistoryStore.DefaultRetention,
            sp.GetRequiredService<ILogger<FileSystemSettingsHistoryStore>>()));
        _ = services.AddSingleton<ISettingsService>(sp => new JsonSettingsService(
            AppPaths.GetSettingsPath(),
            sp.GetRequiredService<ILogger<JsonSettingsService>>(),
            sp.GetRequiredService<ISettingsHistoryStore>(),
            sp.GetRequiredService<TimeProvider>()));
        _ = services.AddSingleton<SettingsValidator>();
        _ = services.AddTransient<AppInitializer>();

        return services;
    }
}
