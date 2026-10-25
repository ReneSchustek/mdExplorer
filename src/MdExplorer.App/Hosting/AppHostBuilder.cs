using MdExplorer.App.Logging;
using MdExplorer.App.Services;
using MdExplorer.App.Services.Help;
using MdExplorer.App.ViewModels;
using MdExplorer.App.ViewModels.Help;
using MdExplorer.App.ViewModels.Settings;
using MdExplorer.App.Views;
using MdExplorer.App.Views.Help;
using MdExplorer.App.Views.Panels;
using MdExplorer.App.Views.Settings;
using MdExplorer.Core;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.DependencyInjection;
using MdExplorer.Core.Settings;
using MdExplorer.Data;
using MdExplorer.Data.Options;
using MdExplorer.App.ViewModels.Graph;
using MdExplorer.App.Views.Graph;
using MdExplorer.Graph.DependencyInjection;
using MdExplorer.Graph.Options;
using MdExplorer.Graph.Services;
using MdExplorer.Indexer.Abstractions;
using MdExplorer.Indexer.DependencyInjection;
using MdExplorer.Indexer.Options;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.DependencyInjection;
using MdExplorer.Parser.Options;
using MdExplorer.Search.DependencyInjection;
using MdExplorer.Search.Options;
using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.DependencyInjection;
using MdExplorer.TagCloud.Options;
using MdExplorer.TagCloud.ViewModels;
using MdExplorer.TagCloud.Views;
using MdExplorer.Update.DependencyInjection;
using MdExplorer.Update.Options;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;

using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace MdExplorer.App.Hosting;

/// <summary>
/// Baut den Generic Host der Anwendung: Logging, Optionen, DI-Registrierung der Schichten.
/// Verwendet Factory-Lambdas für UI-Klassen, damit der Analyzer den Constructor-Aufruf sieht
/// und CA1812 nicht greift.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class AppHostBuilder
{
    /// <summary>
    /// Einstellungen des Hosts. Die Konfiguration liest er aus <c>appsettings.json</c> neben der
    /// Programmdatei und aus Umgebungsvariablen; ohne festes Verzeichnis hinge die Datei davon
    /// ab, aus welchem Ordner die Anwendung gestartet wurde.
    /// </summary>
    public static HostApplicationBuilderSettings Settings() => new() { ContentRootPath = AppContext.BaseDirectory };

    /// <summary>
    /// Erzeugt einen vollständig konfigurierten <see cref="IHost"/>.
    /// </summary>
    public static IHost Build() => Build(Settings());

    /// <summary>
    /// Erzeugt den Host mit eigenen Einstellungen. In der Umgebung „Development“ prüft der
    /// Container Lebensdauern bei jeder Auflösung; die Tests bauen den Host deshalb so.
    /// </summary>
    /// <param name="settings">Einstellungen des Host-Builders.</param>
    public static IHost Build(HostApplicationBuilderSettings settings) => CreateBuilder(settings).Build();

    /// <summary>
    /// Stellt den Builder mit allen Registrierungen bereit, ohne ihn zu bauen. Die Tests lesen
    /// daraus jede Registrierung und lösen sie einzeln auf: Die Prüfung beim Bauen erreicht
    /// Fabrik-Registrierungen nicht, und davon gibt es hier viele.
    /// </summary>
    /// <param name="settings">Einstellungen des Host-Builders.</param>
    internal static HostApplicationBuilder CreateBuilder(HostApplicationBuilderSettings settings)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(settings);

        // Ohne die Standardausgaben: Jeder ILogger schreibt in den statischen Serilog-Logger, den
        // App.OnStartup vorher gebaut hat, und damit in Datei, Debug-Ausgabe und Log-Viewer.
        _ = builder.Logging.ClearProviders();
        _ = builder.Services.AddSerilog();

        _ = builder.Services
            .AddOptions<DatabaseOptions>()
            .Configure(options => options.DatabasePath = AppPaths.Default.DatabasePath)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = builder.Services
            .AddOptions<IndexerOptions>()
            .Bind(builder.Configuration.GetSection(IndexerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = builder.Services
            .AddOptions<ParserOptions>()
            .Bind(builder.Configuration.GetSection(ParserOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = builder.Services
            .AddOptions<SearchOptions>()
            .Bind(builder.Configuration.GetSection(SearchOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = builder.Services
            .AddOptions<TagCloudOptions>()
            .Bind(builder.Configuration.GetSection(TagCloudOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = builder.Services
            .AddOptions<GraphOptions>()
            .Bind(builder.Configuration.GetSection(GraphOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = builder.Services
            .AddOptions<UpdateOptions>()
            .Bind(builder.Configuration.GetSection(UpdateOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = builder.Services.AddCore();
        _ = builder.Services.AddData();
        _ = builder.Services.AddIndexer();
        _ = builder.Services.AddParser();
        _ = builder.Services.AddSearch();
        _ = builder.Services.AddTagCloud();
        _ = builder.Services.AddGraph();
        _ = builder.Services.AddUpdate();

        _ = builder.Services.AddAppViewModels();
        _ = builder.Services.AddAppWindows();

        // Erst als Singleton, dann als Hosted Service auf dieselbe Instanz: Wer die Brücke
        // anfordert, bekommt das Objekt, das der Host startet und stoppt.
        _ = builder.Services.AddSingleton<SettingsChangeBridge>();
        _ = builder.Services.AddHostedService(sp => sp.GetRequiredService<SettingsChangeBridge>());

        _ = builder.Services.AddSingleton<IndexerProgressBridge>();
        _ = builder.Services.AddHostedService(sp => sp.GetRequiredService<IndexerProgressBridge>());

        _ = builder.Services.AddHostedService(sp => new UpdateCheckBackgroundService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IMessenger>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<UpdateCheckBackgroundService>>()));

        return builder;
    }
}
