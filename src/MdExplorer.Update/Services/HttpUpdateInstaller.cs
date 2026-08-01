using System.Diagnostics;
using System.Security.Cryptography;
using MdExplorer.Update.Abstractions;
using MdExplorer.Update.Models;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Update.Services;

/// <summary>
/// Lädt das Installationspaket über HTTP in ein eigenes Verzeichnis, prüft es gegen den
/// veröffentlichten SHA-256-Wert und startet es auf Anforderung.
/// <para>
/// Der Installer ist nicht signiert. Die Prüfsumme ist damit der einzige Beleg, dass die
/// geladene Datei die veröffentlichte ist — deshalb wird ohne Prüfwert gar nicht erst geladen
/// und bei Abweichung die Datei gelöscht statt gestartet.
/// </para>
/// </summary>
public sealed partial class HttpUpdateInstaller : IUpdateInstaller
{
    private const int BufferSize = 81920;

    private readonly HttpClient _httpClient;
    private readonly string _downloadDirectory;
    private readonly ILogger<HttpUpdateInstaller> _logger;

    /// <summary>Erzeugt den Installer.</summary>
    /// <param name="httpClient">Client für den Paket-Download (nicht der API-Client).</param>
    /// <param name="downloadDirectory">Verzeichnis für heruntergeladene Pakete.</param>
    /// <param name="logger">Protokoll.</param>
    public HttpUpdateInstaller(HttpClient httpClient, string downloadDirectory, ILogger<HttpUpdateInstaller> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadDirectory);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _downloadDirectory = downloadDirectory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<UpdateDownloadResult> DownloadAndVerifyAsync(
        UpdateAsset asset,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (!asset.IsVerifiable)
        {
            LogNoChecksum(_logger, asset.FileName);
            return UpdateDownloadResult.Failed(UpdateDownloadStatus.NoChecksumPublished);
        }

        string targetPath;
        try
        {
            _ = Directory.CreateDirectory(_downloadDirectory);
            targetPath = Path.Combine(_downloadDirectory, Path.GetFileName(asset.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogStorageFailed(_logger, ex);
            return UpdateDownloadResult.Failed(UpdateDownloadStatus.StorageFailed);
        }

        try
        {
            await DownloadToFileAsync(asset.DownloadUrl, targetPath, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryDelete(targetPath);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException)
        {
            LogDownloadFailed(_logger, ex);
            TryDelete(targetPath);
            return UpdateDownloadResult.Failed(UpdateDownloadStatus.DownloadFailed);
        }

        string actual = await ComputeSha256Async(targetPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actual, asset.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            LogChecksumMismatch(_logger, asset.FileName, asset.ExpectedSha256 ?? string.Empty, actual);
            TryDelete(targetPath);
            return UpdateDownloadResult.Failed(UpdateDownloadStatus.ChecksumMismatch);
        }

        LogVerified(_logger, asset.FileName);
        return UpdateDownloadResult.Verified(targetPath);
    }

    /// <inheritdoc />
    public bool StartInstaller(string installerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            LogInstallerStarted(_logger, installerPath);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            // Start verweigert (z. B. durch SmartScreen abgebrochen): die Anwendung läuft
            // unverändert weiter, der Nutzer kann die Datei selbst ausführen.
            LogInstallerStartFailed(_logger, ex);
            return false;
        }
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
            // Aufräumen ist Kür: bleibt die Datei liegen, wird sie beim nächsten
            // Versuch überschrieben. Kein Grund, den Vorgang scheitern zu lassen.
        }
    }

    private static async Task CopyWithProgressAsync(
        Stream source,
        Stream target,
        long? total,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[BufferSize];
        long written = 0;
        int lastPercent = -1;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            written += read;

            if (progress is null || total is null or 0)
            {
                continue;
            }

            // Nur bei tatsächlichem Prozentwechsel melden — sonst flutet der Fortschritt die UI.
            int percent = (int)(written * 100 / total.Value);
            if (percent != lastPercent)
            {
                lastPercent = percent;
                progress.Report(percent);
            }
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

        long? total = response.Content.Headers.ContentLength;
        Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (source.ConfigureAwait(false))
        {
            FileStream target = new(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
            await using (target.ConfigureAwait(false))
            {
                await CopyWithProgressAsync(source, target, total, progress, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
