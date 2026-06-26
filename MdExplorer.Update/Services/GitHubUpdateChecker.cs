using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MdExplorer.Update.Abstractions;
using MdExplorer.Update.Models;
using MdExplorer.Update.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MdExplorer.Update.Services;

/// <summary>
/// Fragt die GitHub-Releases-API (<c>releases/latest</c>) nach der neuesten Veröffentlichung
/// und vergleicht deren Tag mit der installierten Version. Vorgeschaltet ist eine Throttle-Logik
/// über <see cref="IUpdateCheckJournal"/>: innerhalb des konfigurierten Intervalls wird gar nicht
/// erst über das Netz gegangen. Sämtliche Netz- und Parser-Fehler werden zu
/// <see cref="UpdateCheckStatus.Failed"/> degradiert — der Aufrufer bekommt nie eine Ausnahme.
/// </summary>
public sealed partial class GitHubUpdateChecker : IUpdateChecker
{
    private readonly HttpClient _httpClient;
    private readonly UpdateOptions _options;
    private readonly IAppVersionProvider _versionProvider;
    private readonly IUpdateCheckJournal _journal;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GitHubUpdateChecker> _logger;

    /// <summary>Erzeugt den Checker und löst seine Abhängigkeiten auf.</summary>
    public GitHubUpdateChecker(
        HttpClient httpClient,
        IOptions<UpdateOptions> options,
        IAppVersionProvider versionProvider,
        IUpdateCheckJournal journal,
        TimeProvider timeProvider,
        ILogger<GitHubUpdateChecker> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(versionProvider);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options.Value;
        _versionProvider = versionProvider;
        _journal = journal;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        SemanticVersion current = _versionProvider.CurrentVersion;

        if (await IsThrottledAsync(cancellationToken).ConfigureAwait(false))
        {
            LogThrottled(_logger, _options.CheckIntervalHours);
            return UpdateCheckResult.Skipped(current);
        }

        GitHubRelease? release;
        try
        {
            string requestUri = string.Concat(
                "repos/",
                Uri.EscapeDataString(_options.RepositoryOwner),
                "/",
                Uri.EscapeDataString(_options.RepositoryName),
                "/releases/latest");
            release = await _httpClient
                .GetFromJsonAsync<GitHubRelease>(requestUri, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or NotSupportedException or UriFormatException)
        {
            // Kein Netz, Timeout, ungültige Antwort: bewusst nicht-fatal.
            LogCheckFailed(_logger, ex);
            return UpdateCheckResult.Failed(current);
        }

        if (release is null || !SemanticVersion.TryParse(release.TagName, out SemanticVersion latest))
        {
            LogUnparsableRelease(_logger, release?.TagName ?? "(leer)");
            return UpdateCheckResult.Failed(current);
        }

        // Erfolgreiche Prüfung: Zeitstempel persistieren, damit der Throttle greift.
        await _journal.WriteLastCheckAsync(_timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);

        if (latest > current)
        {
            LogUpdateAvailable(_logger, latest, current);
            Uri releaseUrl = Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out Uri? htmlUri)
                ? htmlUri
                : BuildReleasesPageUrl();
            return UpdateCheckResult.Available(current, latest, releaseUrl);
        }

        LogUpToDate(_logger, current);
        return UpdateCheckResult.UpToDate(current, latest);
    }

    private async Task<bool> IsThrottledAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset? lastCheck = await _journal.ReadLastCheckAsync(cancellationToken).ConfigureAwait(false);
        if (lastCheck is null)
        {
            return false;
        }
        TimeSpan elapsed = _timeProvider.GetUtcNow() - lastCheck.Value;
        return elapsed < TimeSpan.FromHours(_options.CheckIntervalHours);
    }

    private Uri BuildReleasesPageUrl()
    {
        // Bewusst aus Schema + Host zusammengesetzt statt als URI-Literal (S1075).
        UriBuilder builder = new("https", "github.com")
        {
            Path = string.Concat(_options.RepositoryOwner, "/", _options.RepositoryName, "/releases/latest"),
        };
        return builder.Uri;
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);

    [LoggerMessage(EventId = 710, Level = LogLevel.Debug, Message = "Update-Prüfung übersprungen — letzte Prüfung jünger als {IntervalHours} h.")]
    private static partial void LogThrottled(ILogger logger, int intervalHours);

    [LoggerMessage(EventId = 711, Level = LogLevel.Debug, Message = "Update-Prüfung fehlgeschlagen — kein Netz oder ungültige Antwort.")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 712, Level = LogLevel.Debug, Message = "Update-Prüfung: Release-Tag nicht interpretierbar: {Tag}")]
    private static partial void LogUnparsableRelease(ILogger logger, string tag);

    [LoggerMessage(EventId = 713, Level = LogLevel.Information, Message = "Update verfügbar: {Latest} (installiert: {Current}).")]
    private static partial void LogUpdateAvailable(ILogger logger, SemanticVersion latest, SemanticVersion current);

    [LoggerMessage(EventId = 714, Level = LogLevel.Debug, Message = "Anwendung ist aktuell (Version {Current}).")]
    private static partial void LogUpToDate(ILogger logger, SemanticVersion current);
}
