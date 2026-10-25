using System.Text;
using System.Text.Json;
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
/// erst über das Netz gegangen. Netz- und Parser-Fehler werden zu
/// <see cref="UpdateCheckStatus.Failed"/>; eine Ausnahme erreicht den Aufrufer nur, wenn er
/// selbst abbricht.
/// </summary>
public sealed partial class GitHubUpdateChecker : IUpdateChecker
{
    /// <summary>Endung, an der das Installationspaket im Release erkannt wird.</summary>
    private const string SetupSuffix = "-setup.exe";

    /// <summary>Endung der Prüfsummen-Datei neben dem Paket.</summary>
    private const string ChecksumSuffix = ".sha256";

    /// <summary>Endung der Signatur-Datei neben dem Paket.</summary>
    private const string SignatureSuffix = ".sig";

    /// <summary>Länge eines SHA-256 in Hex-Schreibweise.</summary>
    private const int Sha256HexLength = 64;

    /// <summary>
    /// Obergrenze der Release-Antwort. Sie ist wenige Kilobyte groß, mit langen Release-Notizen
    /// und vielen Anhängen einige zehn; ein Megabyte lässt reichlich Luft.
    /// </summary>
    private const int MaxReleaseResponseBytes = 1024 * 1024;

    /// <summary>Obergrenze der Prüfsummen-Datei. Sie enthält eine Zeile von rund hundert Zeichen.</summary>
    private const int MaxChecksumBytes = 4096;

    /// <summary>Obergrenze der Signatur. Eine ECDSA-Signatur über P-256 im DER-Format hat höchstens 72 Byte.</summary>
    private const int MaxSignatureBytes = 512;

    /// <summary>Trenner in der Prüfsummen-Datei (Format: <c>&lt;hex&gt;  &lt;dateiname&gt;</c>).</summary>
    private static readonly char[] ChecksumSeparators = [' ', '\t', '\r', '\n'];

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
        return await CompareAsync(current, latest, release, cancellationToken).ConfigureAwait(false);
    }

    private async Task<UpdateCheckResult> CompareAsync(
        SemanticVersion current,
        SemanticVersion latest,
        GitHubRelease release,
        CancellationToken cancellationToken)
    {
        if (latest <= current)
        {
            LogUpToDate(_logger, current);
            return UpdateCheckResult.UpToDate(current, latest);
        }

        LogUpdateAvailable(_logger, latest, current);
        UpdateAsset? asset = await ResolveAssetAsync(release, cancellationToken).ConfigureAwait(false);
        return UpdateCheckResult.Available(current, latest, ResolveReleaseUrl(release), asset);
    }

    /// <summary>
    /// <c>Fetched=false</c> heißt: Netz- oder Parser-Fehler, schon protokolliert und nicht fatal.
    /// <c>Release=null</c> bei <c>Fetched=true</c> heißt: Die API hat mit leerem Inhalt geantwortet.
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
            byte[] body = await LimitedResponseReader
                .ReadAsync(_httpClient, new Uri(requestUri, UriKind.Relative), MaxReleaseResponseBytes, isTrustedSource: null, cancellationToken)
                .ConfigureAwait(false);
            return (true, JsonSerializer.Deserialize<GitHubRelease>(body));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException or UriFormatException)
        {
            // Kein Netz, Timeout, ungültige Antwort: bewusst nicht-fatal.
            LogCheckFailed(_logger, ex);
            return (false, null);
        }
    }

    /// <summary>
    /// Nutzt die <c>html_url</c> des Releases, wenn sie eine Release-Seite des Repositorys ist,
    /// sonst die allgemeine Releases-Seite. Die Adresse landet im Browser des Nutzers.
    /// </summary>
    private Uri ResolveReleaseUrl(GitHubRelease release)
    {
        if (Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out Uri? htmlUri) && IsOwnReleasePage(htmlUri))
        {
            return htmlUri;
        }

        LogUntrustedAddress(_logger, "html_url");
        return BuildReleasesPageUrl();
    }

    private async Task<bool> IsThrottledAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset? lastCheck = await _journal.ReadLastCheckAsync(cancellationToken).ConfigureAwait(false);
        if (lastCheck is null)
        {
            return false;
        }
        DateTimeOffset now = _timeProvider.GetUtcNow();
        // Ein Zeitpunkt in der Zukunft stammt von einer falsch gestellten Uhr oder einem
        // beschädigten Journal. Er gälte sonst so lange als frisch, bis die Uhr ihn einholt.
        if (lastCheck.Value > now)
        {
            return false;
        }

        return now - lastCheck.Value < TimeSpan.FromHours(_options.CheckIntervalHours);
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
    /// (oder <see langword="null"/>); installiert wird dann nicht, der Weg über die
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

        if (!IsOwnReleaseAsset(setupUrl))
        {
            LogUntrustedAddress(_logger, "browser_download_url");
            return null;
        }

        Uri? checksumUrl = FindCompanion(assets, setup.Name, ChecksumSuffix);
        Uri? signatureUrl = FindCompanion(assets, setup.Name, SignatureSuffix);
        ReadOnlyMemory<byte> checksum = await TryReadCompanionAsync(checksumUrl, MaxChecksumBytes, cancellationToken).ConfigureAwait(false);
        ReadOnlyMemory<byte> signature = await TryReadCompanionAsync(signatureUrl, MaxSignatureBytes, cancellationToken).ConfigureAwait(false);
        return new UpdateAsset(setup.Name, setupUrl, ParseChecksum(checksum), signature);
    }

    /// <summary>
    /// Liest den Prüfwert aus der Prüfsummen-Datei. Erwartet wird das übliche Format
    /// <c>&lt;hex&gt;  &lt;dateiname&gt;</c>; genommen wird der erste Token, sofern er wie ein SHA-256 aussieht.
    /// </summary>
    private string? ParseChecksum(ReadOnlyMemory<byte> body)
    {
        if (body.IsEmpty)
        {
            return null;
        }

        string token = Encoding.UTF8.GetString(body.Span).Trim().Split(ChecksumSeparators, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        if (token.Length == Sha256HexLength && token.All(Uri.IsHexDigit))
        {
            return token;
        }

        LogUnparsableChecksum(_logger);
        return null;
    }

    // Eine Begleitdatei zählt nur, wenn sie im selben Release des Repositorys liegt.
    private Uri? FindCompanion(IReadOnlyList<GitHubAsset> assets, string setupName, string suffix)
    {
        string name = setupName + suffix;
        GitHubAsset? companion = assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        if (companion is not null
            && Uri.TryCreate(companion.DownloadUrl, UriKind.Absolute, out Uri? address)
            && IsOwnReleaseAsset(address))
        {
            return address;
        }

        LogMissingCompanion(_logger, setupName, suffix);
        return null;
    }

    // Leer heißt: nicht vorhanden oder nicht ladbar; beides verhindert die Installation.
    private async Task<ReadOnlyMemory<byte>> TryReadCompanionAsync(Uri? address, int maxBytes, CancellationToken cancellationToken)
    {
        if (address is null)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        try
        {
            return await LimitedResponseReader
                .ReadAsync(_httpClient, address, maxBytes, ReleaseAddressPolicy.IsTransferAddress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            LogCompanionFetchFailed(_logger, address.AbsolutePath, ex);
            return ReadOnlyMemory<byte>.Empty;
        }
    }

    private bool IsOwnReleasePage(Uri address) =>
        ReleaseAddressPolicy.IsReleasePage(address, _options.RepositoryOwner, _options.RepositoryName);

    private bool IsOwnReleaseAsset(Uri address) =>
        ReleaseAddressPolicy.IsReleaseAsset(address, _options.RepositoryOwner, _options.RepositoryName);

    /// <summary>
    /// Die drei Felder der Release-Antwort, die die Prüfung braucht. Alle nullbar: Die Antwort
    /// kommt von außen. Ein fehlendes Feld führt zu einem Rückfall statt zu einer Ausnahme:
    /// ohne Tag <see cref="UpdateCheckStatus.Failed"/>, ohne Adresse die allgemeine
    /// Releases-Seite, ohne Anhänge kein Installationspaket.
    /// </summary>
    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset>? Assets);

    /// <summary>Eine angehängte Datei des Releases: Name und direkte Download-Adresse.</summary>
    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("browser_download_url")] string? DownloadUrl);

    [LoggerMessage(EventId = 710, Level = LogLevel.Debug, Message = "Update-Prüfung übersprungen — letzte Prüfung jünger als {IntervalHours} h.")]
    private static partial void LogThrottled(ILogger logger, int intervalHours);

    [LoggerMessage(EventId = 711, Level = LogLevel.Warning, Message = "Update-Prüfung fehlgeschlagen — kein Netz oder ungültige Antwort.")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 712, Level = LogLevel.Warning, Message = "Update-Prüfung: Release-Tag nicht interpretierbar: {Tag}")]
    private static partial void LogUnparsableRelease(ILogger logger, string tag);

    [LoggerMessage(EventId = 713, Level = LogLevel.Information, Message = "Update verfügbar: {Latest} (installiert: {Current}).")]
    private static partial void LogUpdateAvailable(ILogger logger, SemanticVersion latest, SemanticVersion current);

    [LoggerMessage(EventId = 714, Level = LogLevel.Debug, Message = "Anwendung ist aktuell (Version {Current}).")]
    private static partial void LogUpToDate(ILogger logger, SemanticVersion current);

    [LoggerMessage(EventId = 715, Level = LogLevel.Information, Message = "Release enthält kein Installationspaket — nur der Weg über die Release-Seite bleibt.")]
    private static partial void LogNoSetupAsset(ILogger logger);

    [LoggerMessage(EventId = 716, Level = LogLevel.Information, Message = "Zu {FileName} fehlt die Datei {Suffix} im Release — es wird nicht automatisch installiert.")]
    private static partial void LogMissingCompanion(ILogger logger, string fileName, string suffix);

    [LoggerMessage(EventId = 717, Level = LogLevel.Warning, Message = "Prüfsummen-Datei ist nicht interpretierbar.")]
    private static partial void LogUnparsableChecksum(ILogger logger);

    [LoggerMessage(EventId = 718, Level = LogLevel.Warning, Message = "Begleitdatei {Path} des Releases konnte nicht geladen werden.")]
    private static partial void LogCompanionFetchFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 719, Level = LogLevel.Warning, Message = "Adresse im Feld {Field} gehört nicht zum Repository oder nutzt kein https — sie wird nicht verwendet.")]
    private static partial void LogUntrustedAddress(ILogger logger, string field);
}
