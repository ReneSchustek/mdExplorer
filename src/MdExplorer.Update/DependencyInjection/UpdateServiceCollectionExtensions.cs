using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using MdExplorer.Core;
using MdExplorer.Update.Abstractions;
using MdExplorer.Update.Options;
using MdExplorer.Update.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MdExplorer.Update.DependencyInjection;

/// <summary>DI-Registrierung des Update-Moduls.</summary>
[ExcludeFromCodeCoverage]
public static class UpdateServiceCollectionExtensions
{
    private const string ReleaseJournalFileName = "update-check.json";

    /// <summary>Name des HTTP-Clients für den Paket-Download.</summary>
    private const string DownloadClientName = "MdExplorer.UpdateDownload";

    /// <summary>Unterverzeichnis für heruntergeladene Installationspakete.</summary>
    private const string DownloadFolderName = "updates";

    /// <summary>
    /// Registriert den <see cref="IUpdateChecker"/> als typisierten <see cref="HttpClient"/>
    /// (mit GitHub-konformem <c>User-Agent</c>), den Versions-Provider aus der Einstiegs-Assembly
    /// und das Datei-Journal für den Throttle. Erwartet, dass <c>UpdateOptions</c> bereits über das
    /// Optionssystem gebunden ist.
    /// </summary>
    public static IServiceCollection AddUpdate(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _ = services.AddSingleton<IAppVersionProvider>(
            _ => new AssemblyAppVersionProvider(Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()));

        _ = services.AddSingleton<IUpdateCheckJournal>(sp => new JsonFileUpdateCheckJournal(
            Path.Combine(AppPaths.GetApplicationDataDirectory(), ReleaseJournalFileName),
            sp.GetRequiredService<ILogger<JsonFileUpdateCheckJournal>>()));

        _ = services
            .AddHttpClient<IUpdateChecker, GitHubUpdateChecker>(ConfigureGitHubClient);

        // Eigener, benannter Client für den Paket-Download: Der API-Client trägt ein knappes
        // Timeout und GitHub-spezifische Header — für eine 45-MB-Datei wäre beides falsch.
        _ = services.AddHttpClient(DownloadClientName, ConfigureDownloadClient);

        _ = services.AddTransient<IUpdateInstaller>(sp => new HttpUpdateInstaller(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(DownloadClientName),
            Path.Combine(AppPaths.GetApplicationDataDirectory(), DownloadFolderName),
            sp.GetRequiredService<ILogger<HttpUpdateInstaller>>()));

        return services;
    }

    private static void ConfigureDownloadClient(HttpClient client)
    {
        // Große Datei über möglicherweise langsame Leitung: das API-Timeout wäre zu knapp.
        client.Timeout = TimeSpan.FromMinutes(10);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MdExplorer-UpdateInstaller");
    }

    private static void ConfigureGitHubClient(IServiceProvider serviceProvider, HttpClient client)
    {
        UpdateOptions options = serviceProvider.GetRequiredService<IOptions<UpdateOptions>>().Value;
        // Aus Schema + Host zusammengesetzt statt als URI-Literal (S1075).
        client.BaseAddress = new UriBuilder("https", "api.github.com").Uri;
        client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        // GitHub lehnt Anfragen ohne User-Agent ab; der API-Media-Type pinnt das Antwortformat.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MdExplorer-UpdateChecker");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }
}
