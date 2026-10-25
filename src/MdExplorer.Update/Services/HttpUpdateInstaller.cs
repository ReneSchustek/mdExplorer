using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using MdExplorer.Update.Abstractions;
using MdExplorer.Update.Models;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Update.Services;

/// <summary>
/// Lädt das Installationspaket über HTTP in ein eigenes Verzeichnis, prüft es gegen den
/// veröffentlichten SHA-256-Wert und startet es auf Anforderung.
/// <para>
/// Die Prüfsumme kommt aus demselben Release wie das Paket und erkennt deshalb nur einen
/// abgebrochenen oder unterwegs veränderten Download. Die Herkunft belegt die Signatur, geprüft
/// gegen den eingebauten Schlüssel (<see cref="ReleaseSignatureVerifier"/>). Geladen wird nur mit
/// beidem und nur von den Adressen, die <see cref="ReleaseAddressPolicy"/> zulässt; bei jeder
/// Abweichung wird die Datei gelöscht statt gestartet.
/// </para>
/// </summary>
public sealed partial class HttpUpdateInstaller : IUpdateInstaller
{
    // Dieselbe Größe wie der Kopierpuffer von .NET, knapp unter der Grenze von 85.000 Byte,
    // ab der ein Array im Large Object Heap landet.
    private const int BufferSize = 81920;

    private const string InstallerExtension = ".exe";

    private readonly HttpClient _httpClient;
    private readonly string _downloadDirectory;
    private readonly DownloadLimits _limits;
    private readonly ReleaseSignatureVerifier _signature;
    private readonly TimeProvider _timeProvider;
    private readonly Action<string> _launch;
    private readonly ILogger<HttpUpdateInstaller> _logger;
    private VerifiedInstaller? _verified;

    /// <summary>Erzeugt den Installer.</summary>
    /// <param name="httpClient">Client für den Paket-Download (nicht der API-Client).</param>
    /// <param name="downloadDirectory">Verzeichnis für heruntergeladene Pakete.</param>
    /// <param name="timeProvider">Uhr für die Stillstandserkennung.</param>
    /// <param name="logger">Protokoll.</param>
    public HttpUpdateInstaller(
        HttpClient httpClient,
        string downloadDirectory,
        TimeProvider timeProvider,
        ILogger<HttpUpdateInstaller> logger)
        : this(httpClient, downloadDirectory, DownloadLimits.Default, ReleaseSignatureVerifier.FromEmbeddedKey(), timeProvider, StartWithShell, logger)
    {
    }

    /// <summary>Erzeugt den Installer mit eigenen Grenzen, Schlüssel und Prozessstart (Tests).</summary>
    internal HttpUpdateInstaller(
        HttpClient httpClient,
        string downloadDirectory,
        DownloadLimits limits,
        ReleaseSignatureVerifier signature,
        TimeProvider timeProvider,
        Action<string> launch,
        ILogger<HttpUpdateInstaller> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadDirectory);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _downloadDirectory = downloadDirectory;
        _limits = limits;
        _signature = signature;
        _timeProvider = timeProvider;
        _launch = launch;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<UpdateDownloadResult> DownloadAndVerifyAsync(
        UpdateAsset asset,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        _verified = null;

        UpdateDownloadStatus? refusal = Refuse(asset);
        if (refusal is not null)
        {
            return UpdateDownloadResult.Failed(refusal.Value);
        }

        string? targetPath = TryPrepareTarget(asset.FileName);
        if (targetPath is null)
        {
            return UpdateDownloadResult.Failed(UpdateDownloadStatus.StorageFailed);
        }

        UpdateDownloadStatus status = await DownloadAsync(asset.DownloadUrl, targetPath, progress, cancellationToken).ConfigureAwait(false);
        if (status == UpdateDownloadStatus.Verified)
        {
            status = await VerifyAsync(asset, targetPath, cancellationToken).ConfigureAwait(false);
        }

        return status == UpdateDownloadStatus.Verified
            ? UpdateDownloadResult.Verified(targetPath)
            : UpdateDownloadResult.Failed(status);
    }

    /// <inheritdoc />
    public bool StartInstaller(string installerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);

        VerifiedInstaller? verified = _verified;
        if (verified is null || !string.Equals(Path.GetFullPath(installerPath), verified.Path, StringComparison.OrdinalIgnoreCase))
        {
            LogNotVerified(_logger, installerPath);
            return false;
        }

        try
        {
            return StartUnchanged(verified);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // Start verweigert (z. B. durch SmartScreen abgebrochen) oder Datei nicht mehr
            // lesbar: die Anwendung läuft unverändert weiter, der Nutzer kann neu laden.
            LogInstallerStartFailed(_logger, ex);
            return false;
        }
    }

    private static void ThrowIfIncomplete(long? announced, long written)
    {
        if (announced is not null && written != announced)
        {
            throw new HttpRequestException(
                HttpRequestError.ResponseEnded,
                $"Angekündigt waren {announced} Byte, geliefert wurden {written}.",
                inner: null,
                HttpStatusCode.OK);
        }
    }

    private static void StartWithShell(string installerPath)
    {
        using Process? process = Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
    }

    /// <summary>Berechnet den SHA-256 der Datei in Hex-Großschreibung.</summary>
    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Aufräumen ist Kür: Ein liegengebliebenes Paket räumt der nächste Download weg,
            // und starten lässt es sich ohne erneute Prüfung nicht.
        }
    }

    [LoggerMessage(EventId = 720, Level = LogLevel.Warning, Message = "Update-Paket {FileName} trägt keinen veröffentlichten Prüfwert — es wird nicht installiert.")]
    private static partial void LogNoChecksum(ILogger logger, string fileName);

    [LoggerMessage(EventId = 721, Level = LogLevel.Warning, Message = "Update-Paket konnte nicht abgelegt werden.")]
    private static partial void LogStorageFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 722, Level = LogLevel.Warning, Message = "Download des Update-Pakets fehlgeschlagen.")]
    private static partial void LogDownloadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 723, Level = LogLevel.Error, Message = "Prüfsumme von {FileName} weicht ab (erwartet {Expected}, berechnet {Actual}) — Datei verworfen.")]
    private static partial void LogChecksumMismatch(ILogger logger, string fileName, string expected, string actual);

    [LoggerMessage(EventId = 724, Level = LogLevel.Information, Message = "Update-Paket {FileName} geladen und Prüfsumme bestätigt.")]
    private static partial void LogVerified(ILogger logger, string fileName);

    [LoggerMessage(EventId = 725, Level = LogLevel.Information, Message = "Installationsprogramm gestartet: {Path}")]
    private static partial void LogInstallerStarted(ILogger logger, string path);

    [LoggerMessage(EventId = 726, Level = LogLevel.Warning, Message = "Installationsprogramm konnte nicht gestartet werden.")]
    private static partial void LogInstallerStartFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 727, Level = LogLevel.Warning, Message = "Update-Paket {FileName} von {Host} abgelehnt: nur Programme von den Release-Adressen werden geladen.")]
    private static partial void LogRefused(ILogger logger, string fileName, string host);

    [LoggerMessage(EventId = 731, Level = LogLevel.Warning, Message = "Update-Paket {FileName} wird nicht installiert: keine Signatur (Schlüssel in dieser Fassung vorhanden: {KeyPresent}).")]
    private static partial void LogNoSignature(ILogger logger, string fileName, bool keyPresent);

    [LoggerMessage(EventId = 732, Level = LogLevel.Error, Message = "Signatur von {FileName} passt nicht zum Paket — Datei verworfen.")]
    private static partial void LogSignatureInvalid(ILogger logger, string fileName);

    [LoggerMessage(EventId = 728, Level = LogLevel.Warning, Message = "Start abgelehnt: {Path} ist kein in diesem Lauf geprüftes Paket.")]
    private static partial void LogNotVerified(ILogger logger, string path);

    [LoggerMessage(EventId = 729, Level = LogLevel.Error, Message = "Start abgelehnt: {Path} hat sich seit der Prüfung verändert.")]
    private static partial void LogChangedSinceVerification(ILogger logger, string path);

    [LoggerMessage(EventId = 730, Level = LogLevel.Debug, Message = "Früherer Download-Ordner bleibt vorerst liegen: {Folder}")]
    private static partial void LogCleanupSkipped(ILogger logger, string folder, Exception exception);

    // Die Datei bleibt bis nach dem Start geöffnet und lässt nur Lesen zu. Zwischen der zweiten
    // Prüfung und dem Start kann sie im benutzerbeschreibbaren Ordner so niemand austauschen.
    private bool StartUnchanged(VerifiedInstaller verified)
    {
        using FileStream locked = new(verified.Path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
        string actual = Convert.ToHexString(SHA256.HashData(locked));
        if (!string.Equals(actual, verified.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            _verified = null;
            LogChangedSinceVerification(_logger, verified.Path);
            return false;
        }

        _launch(verified.Path);
        LogInstallerStarted(_logger, verified.Path);
        return true;
    }

    // Ablehnung vor jedem Netzzugriff: ohne Prüfwert oder Signatur, mit fremder Adresse oder mit einem Namen,
    // der kein Programm ist, wird gar nicht erst geladen.
    private UpdateDownloadStatus? Refuse(UpdateAsset asset)
    {
        if (string.IsNullOrWhiteSpace(asset.ExpectedSha256))
        {
            LogNoChecksum(_logger, asset.FileName);
            return UpdateDownloadStatus.NoChecksumPublished;
        }

        if (asset.Signature.IsEmpty || !_signature.IsConfigured)
        {
            LogNoSignature(_logger, asset.FileName, _signature.IsConfigured);
            return UpdateDownloadStatus.NoSignaturePublished;
        }

        if (!ReleaseAddressPolicy.IsTransferAddress(asset.DownloadUrl)
            || !Path.GetFileName(asset.FileName).EndsWith(InstallerExtension, StringComparison.OrdinalIgnoreCase))
        {
            LogRefused(_logger, asset.FileName, asset.DownloadUrl.Host);
            return UpdateDownloadStatus.DownloadFailed;
        }

        return null;
    }

    /// <summary>
    /// Legt für jeden Download einen frischen Unterordner an und räumt frühere weg.
    /// </summary>
    /// <remarks>
    /// Ein Setup, das sich erhöht ausführt, lädt unter Umständen Bibliotheken aus seinem eigenen
    /// Ordner. Im frischen Ordner liegt nichts, was vorher jemand dort abgelegt haben könnte.
    /// </remarks>
    private string? TryPrepareTarget(string fileName)
    {
        try
        {
            _ = Directory.CreateDirectory(_downloadDirectory);
            RemovePreviousDownloads();
            string folder = Directory.CreateDirectory(Path.Combine(_downloadDirectory, Guid.NewGuid().ToString("N"))).FullName;
            return Path.Combine(folder, Path.GetFileName(fileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogStorageFailed(_logger, ex);
            return null;
        }
    }

    private void RemovePreviousDownloads()
    {
        foreach (string folder in Directory.EnumerateDirectories(_downloadDirectory))
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ein Setup, das noch läuft, hält seine Datei offen. Der Ordner bleibt dann bis
                // zum nächsten Download liegen.
                LogCleanupSkipped(_logger, folder, ex);
            }
        }
    }

    private async Task<UpdateDownloadStatus> DownloadAsync(
        Uri downloadUrl,
        string targetPath,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            await DownloadToFileAsync(downloadUrl, targetPath, progress, cancellationToken).ConfigureAwait(false);
            return UpdateDownloadStatus.Verified;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryDelete(targetPath);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or TimeoutException or IOException or UnauthorizedAccessException)
        {
            LogDownloadFailed(_logger, ex);
            TryDelete(targetPath);
            return UpdateDownloadStatus.DownloadFailed;
        }
    }

    private async Task<UpdateDownloadStatus> VerifyAsync(UpdateAsset asset, string targetPath, CancellationToken cancellationToken)
    {
        string? actual = await TryHashAsync(targetPath, cancellationToken).ConfigureAwait(false);
        if (actual is null)
        {
            TryDelete(targetPath);
            return UpdateDownloadStatus.StorageFailed;
        }

        if (!string.Equals(actual, asset.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            LogChecksumMismatch(_logger, asset.FileName, asset.ExpectedSha256 ?? string.Empty, actual);
            TryDelete(targetPath);
            return UpdateDownloadStatus.ChecksumMismatch;
        }

        if (!await HasValidSignatureAsync(targetPath, asset.Signature).ConfigureAwait(false))
        {
            LogSignatureInvalid(_logger, asset.FileName);
            TryDelete(targetPath);
            return UpdateDownloadStatus.SignatureInvalid;
        }

        _verified = new VerifiedInstaller(Path.GetFullPath(targetPath), actual);
        LogVerified(_logger, asset.FileName);
        return UpdateDownloadStatus.Verified;
    }

    private async Task<string?> TryHashAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ein Virenscanner sperrt eine frisch geschriebene .exe gern für einen Moment.
            LogStorageFailed(_logger, ex);
            return null;
        }
    }

    private async Task<bool> HasValidSignatureAsync(string path, ReadOnlyMemory<byte> signature)
    {
        FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            return _signature.Verify(stream, signature);
        }
    }

    private async Task DownloadToFileAsync(
        Uri downloadUrl,
        string targetPath,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient
            .GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        _ = response.EnsureSuccessStatusCode();
        LimitedResponseReader.ThrowIfUntrusted(response, ReleaseAddressPolicy.IsTransferAddress);

        long? total = response.Content.Headers.ContentLength;
        if (total > _limits.MaxBytes)
        {
            throw LimitedResponseReader.TooLarge(_limits.MaxBytes);
        }

        Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (source.ConfigureAwait(false))
        {
            FileStream target = new(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
            await using (target.ConfigureAwait(false))
            {
                long written = await CopyWithProgressAsync(source, target, total, progress, cancellationToken).ConfigureAwait(false);
                ThrowIfIncomplete(total, written);
            }
        }
    }

    private async Task<long> CopyWithProgressAsync(
        Stream source,
        Stream target,
        long? total,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[BufferSize];
        DownloadProgress reporter = new(total, progress);
        using StallGuard stall = new(_limits.StallTimeout, _timeProvider, cancellationToken);
        int read;
        while ((read = await stall.ReadAsync(source, buffer).ConfigureAwait(false)) > 0)
        {
            // Ohne Längenangabe ist das die einzige Grenze; ein endloser Strom füllte sonst die Platte.
            if (reporter.Written + read > _limits.MaxBytes)
            {
                throw LimitedResponseReader.TooLarge(_limits.MaxBytes);
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            reporter.Add(read);
        }

        return reporter.Written;
    }

    /// <summary>Das zuletzt geprüfte Paket: nur dieses darf gestartet werden.</summary>
    private sealed record VerifiedInstaller(string Path, string Sha256);

    /// <summary>Zählt die geschriebenen Byte und meldet den Fortschritt nur bei neuem Prozentwert.</summary>
    private sealed class DownloadProgress(long? total, IProgress<int>? progress)
    {
        private int _lastPercent = -1;

        public long Written { get; private set; }

        public void Add(int read)
        {
            Written += read;
            if (progress is null || total is null or 0)
            {
                return;
            }

            // Nur bei tatsächlichem Prozentwechsel melden, sonst flutet der Fortschritt die UI.
            int percent = (int)(Written * 100 / total.Value);
            if (percent != _lastPercent)
            {
                _lastPercent = percent;
                progress.Report(percent);
            }
        }
    }
}
