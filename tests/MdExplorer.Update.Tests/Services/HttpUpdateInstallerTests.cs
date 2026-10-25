using System.Net;
using System.Security.Cryptography;
using MdExplorer.Update.Models;
using MdExplorer.Update.Services;
using MdExplorer.Update.Tests.Fakes;

namespace MdExplorer.Update.Tests.Services;

/// <summary>
/// Der Installer lädt das Setup nur mit veröffentlichter Prüfsumme und Signatur, behält es nur,
/// wenn beide passen, und schreibt nie außerhalb seines Ablageordners.
/// <para>
/// Die Prüfsumme erkennt einen verfälschten Download, die Signatur ein Paket, das nicht vom
/// Herausgeber stammt. Diese Tests halten vor allem fest, dass bei jeder Abweichung nichts
/// liegen bleibt, was sich später starten ließe.
/// </para>
/// </summary>
public sealed class HttpUpdateInstallerTests : IDisposable
{
    private readonly InstallerTestBench _bench = new();

    public void Dispose() => _bench.Dispose();

    [Fact]
    public async Task DownloadAndVerifyAsync_WithoutPublishedChecksum_DoesNotDownload()
    {
        StubHttpMessageHandler handler = StubHttpMessageHandler.Throwing(
            new InvalidOperationException("Ohne Prüfwert darf gar nicht erst geladen werden."));
        HttpUpdateInstaller sut = _bench.CreateSut(handler);
        UpdateAsset asset = new(InstallerTestBench.PackageName, InstallerTestBench.PackageAddress, expectedSha256: null);

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(asset, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.NoChecksumPublished, result.Status);
        Assert.Null(result.InstallerPath);
        Assert.False(result.IsVerified);
        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WithoutSignature_DoesNotDownload()
    {
        byte[] content = "Ein Paket ohne Signatur."u8.ToArray();
        StubHttpMessageHandler handler = InstallerTestBench.WithPayload(content);
        HttpUpdateInstaller sut = _bench.CreateSut(handler);
        UpdateAsset asset = new(InstallerTestBench.PackageName, InstallerTestBench.PackageAddress, InstallerTestBench.Sha256Of(content));

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(asset, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.NoSignaturePublished, result.Status);
        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WhenThisBuildHasNoKey_DoesNotDownload()
    {
        byte[] content = "Signiert, aber niemand kann prüfen."u8.ToArray();
        StubHttpMessageHandler handler = InstallerTestBench.WithPayload(content);
        HttpUpdateInstaller sut = _bench.CreateSut(handler, verifier: new ReleaseSignatureVerifier(publicKeyPem: null));

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(_bench.AssetFor(content), progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.NoSignaturePublished, result.Status);
        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WithMatchingChecksumAndSignature_KeepsFileAndReportsVerified()
    {
        byte[] content = "Ein Installationspaket."u8.ToArray();
        HttpUpdateInstaller sut = _bench.CreateSut(InstallerTestBench.WithPayload(content));

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(_bench.AssetFor(content), progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.Verified, result.Status);
        Assert.True(result.IsVerified);
        string path = Assert.IsType<string>(result.InstallerPath);
        Assert.Equal(content, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_OnChecksumMismatch_DeletesFileAndReportsMismatch()
    {
        byte[] content = "Ein manipuliertes Paket."u8.ToArray();
        HttpUpdateInstaller sut = _bench.CreateSut(InstallerTestBench.WithPayload(content));
        // Prüfwert eines anderen Inhalts: genau der Fall, gegen den die Prüfung schützt.
        UpdateAsset asset = _bench.AssetFor(content) with { ExpectedSha256 = InstallerTestBench.Sha256Of("Das Original."u8.ToArray()) };

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(asset, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.ChecksumMismatch, result.Status);
        Assert.Null(result.InstallerPath);
        // Die Datei darf nicht liegen bleiben, sonst könnte sie jemand von Hand starten.
        Assert.Empty(_bench.StoredFiles());
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WithChecksumMatchingButSignatureOfOtherContent_DeletesFile()
    {
        // Wer das Release übernimmt, liefert Paket und passende Prüfsumme; die Signatur kann er
        // ohne den privaten Schlüssel nur von einem anderen, echten Paket übernehmen.
        byte[] content = "Ein untergeschobenes Paket."u8.ToArray();
        HttpUpdateInstaller sut = _bench.CreateSut(InstallerTestBench.WithPayload(content));
        UpdateAsset asset = _bench.AssetFor(content) with { Signature = _bench.Sign("Das echte Paket."u8.ToArray()) };

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(asset, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.SignatureInvalid, result.Status);
        Assert.Empty(_bench.StoredFiles());
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WithSignatureOfAnotherKey_DeletesFile()
    {
        byte[] content = "Signiert mit einem fremden Schlüssel."u8.ToArray();
        using ECDsa foreignKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        HttpUpdateInstaller sut = _bench.CreateSut(InstallerTestBench.WithPayload(content));
        UpdateAsset asset = _bench.AssetFor(content) with
        {
            Signature = foreignKey.SignData(content, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence),
        };

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(asset, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.SignatureInvalid, result.Status);
        Assert.Empty(_bench.StoredFiles());
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_ChecksumComparison_IgnoresCase()
    {
        byte[] content = "Klein- und Großschreibung des Hex-Werts darf nicht entscheiden."u8.ToArray();
        HttpUpdateInstaller sut = _bench.CreateSut(InstallerTestBench.WithPayload(content));
        // Kleinschreibung direkt erzeugen statt nachträglich umwandeln: so bleibt der
        // Prüffall erhalten, ohne den Umweg über eine kulturabhängige Umwandlung.
        UpdateAsset asset = _bench.AssetFor(content) with { ExpectedSha256 = Convert.ToHexStringLower(SHA256.HashData(content)) };

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(asset, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.Verified, result.Status);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_OnHttpError_ReportsDownloadFailedAndLeavesNoFile()
    {
        HttpUpdateInstaller sut = _bench.CreateSut(StubHttpMessageHandler.WithStatus(HttpStatusCode.NotFound));

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(_bench.AssetFor("egal"u8.ToArray()), progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.DownloadFailed, result.Status);
        Assert.Null(result.InstallerPath);
        Assert.Empty(_bench.StoredFiles());
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_OnTransportFailure_ReportsDownloadFailed()
    {
        HttpUpdateInstaller sut = _bench.CreateSut(StubHttpMessageHandler.Throwing(new HttpRequestException("kein Netz")));

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(_bench.AssetFor("egal"u8.ToArray()), progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.DownloadFailed, result.Status);
        Assert.Empty(_bench.StoredFiles());
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WhenCancelled_ThrowsAndLeavesNoFile()
    {
        byte[] content = InstallerTestBench.Payload(512 * 1024);
        HttpUpdateInstaller sut = _bench.CreateSut(InstallerTestBench.WithPayload(content));
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.DownloadAndVerifyAsync(_bench.AssetFor(content), progress: null, cts.Token));

        Assert.Empty(_bench.StoredFiles());
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_ReportsProgressOnlyOnPercentChange()
    {
        // 16 MB bei 80 KB Puffer sind rund 200 Lesevorgänge, also mehr als es Prozentwerte gibt.
        // Ohne die Beschränkung auf einen Wechsel kämen gut 200 Meldungen, viele davon doppelt.
        byte[] content = InstallerTestBench.Payload(16 * 1024 * 1024);
        HttpUpdateInstaller sut = _bench.CreateSut(InstallerTestBench.WithPayload(content));
        RecordingProgress progress = new();

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(_bench.AssetFor(content), progress, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.Verified, result.Status);
        Assert.InRange(progress.Reported.Count, 1, 101);
        Assert.Equal(progress.Reported.Order(), progress.Reported);
        Assert.Equal(progress.Reported.Count, progress.Reported.Distinct().Count());
        Assert.Equal(100, progress.Reported[^1]);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WithoutContentLength_StillVerifies()
    {
        // Ohne Content-Length kann kein Prozentwert berechnet werden; der Download muss
        // trotzdem gelingen und die Prüfung greifen.
        byte[] content = "Antwort ohne Längenangabe."u8.ToArray();
        HttpUpdateInstaller sut = _bench.CreateSut(StubHttpMessageHandler.WithStream(content, setContentLength: false));
        RecordingProgress progress = new();

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(_bench.AssetFor(content), progress, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.Verified, result.Status);
        Assert.Empty(progress.Reported);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_UsesOnlyTheFileNamePartOfTheAsset()
    {
        // Ein Pfadanteil im Dateinamen darf nicht dazu führen, dass außerhalb des
        // Download-Verzeichnisses geschrieben wird.
        byte[] content = "Pfadanteil im Namen."u8.ToArray();
        HttpUpdateInstaller sut = _bench.CreateSut(InstallerTestBench.WithPayload(content));
        UpdateAsset asset = _bench.AssetFor(content) with { FileName = Path.Combine("..", "..", "setup.exe") };

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(asset, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.Verified, result.Status);
        string folder = Assert.IsType<string>(Path.GetDirectoryName(result.InstallerPath));
        Assert.Equal(_bench.DownloadDirectory, Path.GetDirectoryName(folder));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_ASecondDownload_LoadsIntoAFreshFolderAndRemovesTheFirst()
    {
        // Ein Setup, das sich erhöht ausführt, lädt unter Umständen Bibliotheken aus seinem
        // Ordner. Im frischen Ordner liegt nichts, was dort vorher jemand abgelegt hat.
        byte[] content = "Zweimal geladen."u8.ToArray();
        HttpUpdateInstaller first = _bench.CreateSut(InstallerTestBench.WithPayload(content));
        HttpUpdateInstaller second = _bench.CreateSut(InstallerTestBench.WithPayload(content));

        UpdateDownloadResult earlier = await first.DownloadAndVerifyAsync(_bench.AssetFor(content), progress: null, TestContext.Current.CancellationToken);
        string earlierFolder = Assert.IsType<string>(Path.GetDirectoryName(earlier.InstallerPath));
        UpdateDownloadResult later = await second.DownloadAndVerifyAsync(_bench.AssetFor(content), progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.Verified, later.Status);
        Assert.NotEqual(earlierFolder, Path.GetDirectoryName(later.InstallerPath));
        Assert.False(Directory.Exists(earlierFolder));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WhenTheFileIsLockedBeforeHashing_ReportsStorageFailure()
    {
        // Ein Virenscanner sperrt die frisch geschriebene .exe. Die Sperre greift, sobald der
        // Download die Datei geschlossen hat, also genau vor der Prüfung.
        byte[] content = "Gesperrt nach dem Schreiben."u8.ToArray();
        using PackageLock packageLock = new(_bench.DownloadDirectory);
        using ClosingSignalStream body = new(content, packageLock.Take);
        HttpUpdateInstaller sut = _bench.CreateSut(InstallerTestBench.WithContent(() => new StreamContent(body)));

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(_bench.AssetFor(content), progress: null, TestContext.Current.CancellationToken);

        Assert.True(packageLock.IsTaken);
        Assert.Equal(UpdateDownloadStatus.StorageFailed, result.Status);
        Assert.Null(result.InstallerPath);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WhenTheDownloadFolderCannotBeCreated_ReportsStorageFailure()
    {
        // Der Ablageordner liegt unter %LOCALAPPDATA% und kann durch eine gleichnamige Datei
        // oder eine Richtlinie blockiert sein. Dann darf nichts geladen und erst recht nichts
        // ausgeführt werden; die Meldung muss den Grund benennen.
        string blockedPath = Path.Combine(_bench.DownloadDirectory, "blockiert");
        await File.WriteAllTextAsync(blockedPath, "keine Mappe", TestContext.Current.CancellationToken);
        HttpUpdateInstaller sut = _bench.CreateSut(StubHttpMessageHandler.WithStatus(HttpStatusCode.OK), blockedPath);

        UpdateDownloadResult outcome = await sut.DownloadAndVerifyAsync(_bench.AssetFor("egal"u8.ToArray()), progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.StorageFailed, outcome.Status);
        Assert.Null(outcome.InstallerPath);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WithoutAsset_Throws()
    {
        HttpUpdateInstaller sut = _bench.CreateSut(StubHttpMessageHandler.WithStatus(HttpStatusCode.OK));

        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.DownloadAndVerifyAsync(null!, progress: null, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Hält jede Meldung sofort fest. <see cref="Progress{T}"/> meldete über den Thread-Pool, und
    /// der Test müsste auf die Rückrufe warten, ohne zu wissen, wie lange.
    /// </summary>
    private sealed class RecordingProgress : IProgress<int>
    {
        public List<int> Reported { get; } = [];

        public void Report(int value) => Reported.Add(value);
    }

    /// <summary>
    /// Liefert den Inhalt und meldet das erste Schließen. Der Installer schließt die Zieldatei vor
    /// dem Antwortstrom; was hier geschieht, liegt damit genau vor der Prüfung.
    /// </summary>
    private sealed class ClosingSignalStream(byte[] content, Action onFirstClose) : MemoryStream(content, writable: false)
    {
        private bool _signalled;

        protected override void Dispose(bool disposing)
        {
            // Hülle und Antwort schließen den Strom beide; gemeldet wird nur das erste Mal.
            if (disposing && !_signalled)
            {
                _signalled = true;
                onFirstClose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>Sperrt das geladene Paket exklusiv, wie es ein Virenscanner tut, bis zum Testende.</summary>
    private sealed class PackageLock(string downloadDirectory) : IDisposable
    {
        private FileStream? _lock;

        public bool IsTaken => _lock is not null;

        public void Take()
        {
            string? package = Directory.GetFiles(downloadDirectory, "*.exe", SearchOption.AllDirectories).SingleOrDefault();
            _lock = package is null ? null : new FileStream(package, FileMode.Open, FileAccess.Read, FileShare.None);
        }

        public void Dispose() => _lock?.Dispose();
    }
}
