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
    /// <summary>Endung, an der das Installationspaket im Release erkannt wird.</summary>
    private const string SetupSuffix = "-setup.exe";

    /// <summary>Endung der Prüfsummen-Datei neben dem Paket.</summary>
    private const string ChecksumSuffix = ".sha256";

    /// <summary>Länge eines SHA-256 in Hex-Schreibweise.</summary>
    private const int Sha256HexLength = 64;

    /// <summary>Trenner in der Prüfsummen-Datei (Format: <c>&lt;hex&gt;  &lt;dateiname&gt;</c>).</summary>
    private static readonly char[] ChecksumSeparators = new[] { ' ', '\t', '\r', '\n' };

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
    public Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken) =>
        CheckForUpdateAsync(force: false, cancellationToken);

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckForUpdateAsync(bool force, CancellationToken cancellationToken)
    {
        SemanticVersion current = _versionProvider.CurrentVersion;

        if (!force && await IsThrottledAsync(cancellationToken).ConfigureAwait(false))
        {
            LogThrottled(_logger, _options.CheckIntervalHours);
            return UpdateCheckResult.Skipped(current);
        }

        (bool fetched, GitHubRelease? release) = await TryFetchLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
        if (!fetched)
        {
            // Netzfehler wurde bereits in TryFetchLatestReleaseAsync geloggt.
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
            UpdateAsset? asset = await ResolveAssetAsync(release, cancellationToken).ConfigureAwait(false);
            return UpdateCheckResult.Available(current, latest, ResolveReleaseUrl(release), asset);
        }

        LogUpToDate(_logger, current);
        return UpdateCheckResult.UpToDate(current, latest);
    }

    /// <summary>
    /// Ruft das neueste Release über die GitHub-API ab. <c>Fetched=false</c> signalisiert einen
    /// bewusst nicht-fatalen Netz-/Parser-Fehler (bereits geloggt); <c>Release=null</c> bei
    /// <c>Fetched=true</c> bedeutet einen leeren Antwort-Body.
    /// </summary>
    private async Task<(bool Fetched, GitHubRelease? Release)> TryFetchLatestReleaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            string requestUri = string.Concat(
                "repos/",
                Uri.EscapeDataString(_options.RepositoryOwner),
                "/",
                Uri.EscapeDataString(_options.RepositoryName),
                "/releases/latest");
            GitHubRelease? release = await _httpClient
                .GetFromJsonAsync<GitHubRelease>(requestUri, cancellationToken)
                .ConfigureAwait(false);
            return (true, release);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or NotSupportedException or UriFormatException)
        {
            // Kein Netz, Timeout, ungültige Antwort: bewusst nicht-fatal.
            LogCheckFailed(_logger, ex);
            return (false, null);
        }
    }

    /// <summary>Nutzt die <c>html_url</c> des Releases, fällt bei ungültiger URL auf die Releases-Seite zurück.</summary>
    private Uri ResolveReleaseUrl(GitHubRelease release) =>
        Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out Uri? htmlUri) ? htmlUri : BuildReleasesPageUrl();

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

    /// <summary>
    /// Sucht im Release das Installationspaket und die zugehörige Prüfsummen-Datei und lädt
    /// deren Inhalt. Fehlt eines von beidem, liefert die Methode ein Paket ohne Prüfwert
    /// (oder <see langword="null"/>) — installiert wird dann nicht, der Weg über die
    /// Release-Seite bleibt.
    /// </summary>
    private async Task<UpdateAsset?> ResolveAssetAsync(GitHubRelease release, CancellationToken cancellationToken)
    {
        IReadOnlyList<GitHubAsset> assets = release.Assets ?? [];

        GitHubAsset? setup = assets.FirstOrDefault(
            a => a.Name is not null && a.Name.EndsWith(SetupSuffix, StringComparison.OrdinalIgnoreCase));
        if (setup?.Name is null || !Uri.TryCreate(setup.DownloadUrl, UriKind.Absolute, out Uri? setupUrl))
        {
            LogNoSetupAsset(_logger);
            return null;
        }

        GitHubAsset? checksum = assets.FirstOrDefault(
            a => string.Equals(a.Name, setup.Name + ChecksumSuffix, StringComparison.OrdinalIgnoreCase));
        if (checksum is null || !Uri.TryCreate(checksum.DownloadUrl, UriKind.Absolute, out Uri? checksumUrl))
        {
            LogNoChecksumAsset(_logger, setup.Name);
            return new UpdateAsset(setup.Name, setupUrl, null);
        }

        string? hash = await TryReadChecksumAsync(checksumUrl, cancellationToken).ConfigureAwait(false);
        return new UpdateAsset(setup.Name, setupUrl, hash);
    }

    /// <summary>
    /// Liest die Prüfsummen-Datei. Erwartet wird das übliche Format <c>&lt;hex&gt;  &lt;dateiname&gt;</c>;
    /// genommen wird der erste Token, sofern er wie ein SHA-256 aussieht.
    /// </summary>
    private async Task<string?> TryReadChecksumAsync(Uri checksumUrl, CancellationToken cancellationToken)
    {
        try
        {
            string content = await _httpClient.GetStringAsync(checksumUrl, cancellationToken).ConfigureAwait(false);
            string token = content.Trim().Split(ChecksumSeparators, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            if (token.Length == Sha256HexLength && token.All(Uri.IsHexDigit))
            {
                return token;
            }

            LogUnparsableChecksum(_logger);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            LogChecksumFetchFailed(_logger, ex);
            return null;
        }
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset>? Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("browser_download_url")] string? DownloadUrl);

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

    [LoggerMessage(EventId = 715, Level = LogLevel.Debug, Message = "Release enthält kein Installationspaket — nur der Weg über die Release-Seite bleibt.")]
    private static partial void LogNoSetupAsset(ILogger logger);

    [LoggerMessage(EventId = 716, Level = LogLevel.Information, Message = "Zu {FileName} gibt es keine Prüfsummen-Datei — es wird nicht automatisch installiert.")]
    private static partial void LogNoChecksumAsset(ILogger logger, string fileName);

    [LoggerMessage(EventId = 717, Level = LogLevel.Warning, Message = "Prüfsummen-Datei ist nicht interpretierbar.")]
    private static partial void LogUnparsableChecksum(ILogger logger);

    [LoggerMessage(EventId = 718, Level = LogLevel.Warning, Message = "Prüfsummen-Datei konnte nicht geladen werden.")]
    private static partial void LogChecksumFetchFailed(ILogger logger, Exception exception);
}
