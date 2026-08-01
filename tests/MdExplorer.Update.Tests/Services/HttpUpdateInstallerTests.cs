using System.Net;
using System.Security.Cryptography;
using MdExplorer.Update.Models;
using MdExplorer.Update.Services;
using MdExplorer.Update.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.Update.Tests.Services;

/// <summary>
/// Prüft den Download- und Verifikationspfad des Installers.
/// <para>
/// Der Installer ist unsigniert; die Prüfsumme ist der einzige Beleg dafür, dass die geladene
/// Datei die veröffentlichte ist. Diese Tests halten deshalb vor allem fest, dass bei jeder
/// Abweichung <b>nichts</b> ausgeführt und die Datei verworfen wird.
/// </para>
/// </summary>
public sealed class HttpUpdateInstallerTests : IDisposable
{
    private readonly string _downloadDirectory;

    // Handler und Client gehören dem Test: Der Prüflauf erzeugt sie, also räumt er sie
    // auch auf. Sonst meldet CA2000 zu Recht, dass Verwerfbares ohne Besitzer entsteht.
    private readonly List<IDisposable> _verwerfbare = [];

    public HttpUpdateInstallerTests()
    {
        _downloadDirectory = Path.Combine(Path.GetTempPath(), "MdExplorerInstallerTests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        foreach (IDisposable einzelnes in _verwerfbare)
        {
            einzelnes.Dispose();
        }

        if (Directory.Exists(_downloadDirectory))
        {
            Directory.Delete(_downloadDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WithoutPublishedChecksum_DoesNotDownload()
    {
        StubHttpMessageHandler handler = StubHttpMessageHandler.Throwing(
            new InvalidOperationException("Ohne Prüfwert darf gar nicht erst geladen werden."));
        HttpUpdateInstaller sut = CreateSut(handler);
        UpdateAsset asset = new("setup.exe", new Uri("https://example.invalid/setup.exe"), ExpectedSha256: null);

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(asset, progress: null, CancellationToken.None);

        Assert.Equal(UpdateDownloadStatus.NoChecksumPublished, result.Status);
        Assert.Null(result.InstallerPath);
        Assert.False(result.IsVerified);
        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WithMatchingChecksum_KeepsFileAndReportsVerified()
    {
        byte[] inhalt = "Ein Installationspaket."u8.ToArray();
        HttpUpdateInstaller sut = CreateSut(WithPayload(inhalt));
        UpdateAsset asset = new("setup.exe", new Uri("https://example.invalid/setup.exe"), Sha256Of(inhalt));

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(asset, progress: null, CancellationToken.None);

        Assert.Equal(UpdateDownloadStatus.Verified, result.Status);
        Assert.True(result.IsVerified);
        Assert.NotNull(result.InstallerPath);
        Assert.True(File.Exists(result.InstallerPath));
        Assert.Equal(inhalt, await File.ReadAllBytesAsync(result.InstallerPath!));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_OnChecksumMismatch_DeletesFileAndReportsMismatch()
    {
        byte[] inhalt = "Ein manipuliertes Paket."u8.ToArray();
        HttpUpdateInstaller sut = CreateSut(WithPayload(inhalt));
        // Prüfwert eines anderen Inhalts: genau der Fall, gegen den die Prüfung schützt.
        UpdateAsset asset = new("setup.exe", new Uri("https://example.invalid/setup.exe"), Sha256Of("Das Original."u8.ToArray()));

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(asset, progress: null, CancellationToken.None);

        Assert.Equal(UpdateDownloadStatus.ChecksumMismatch, result.Status);
        Assert.Null(result.InstallerPath);
        Assert.False(result.IsVerified);
        // Die Datei darf nicht liegen bleiben — sonst könnte sie jemand von Hand starten.
        Assert.Empty(Directory.GetFiles(_downloadDirectory));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_ChecksumComparisonIgnoresCase()
    {
        byte[] inhalt = "Klein- und Großschreibung des Hex-Werts darf nicht entscheiden."u8.ToArray();
        HttpUpdateInstaller sut = CreateSut(WithPayload(inhalt));
        // Kleinschreibung direkt erzeugen statt nachträglich umwandeln: so bleibt der
        // Prüffall erhalten, ohne den Umweg über eine kulturabhängige Umwandlung.
        string kleingeschrieben = Convert.ToHexStringLower(SHA256.HashData(inhalt));
        UpdateAsset asset = new("setup.exe", new Uri("https://example.invalid/setup.exe"), kleingeschrieben);

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(asset, progress: null, CancellationToken.None);

        Assert.Equal(UpdateDownloadStatus.Verified, result.Status);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_OnHttpError_ReportsDownloadFailedAndLeavesNoFile()
    {
        HttpUpdateInstaller sut = CreateSut(StubHttpMessageHandler.WithStatus(HttpStatusCode.NotFound));
        UpdateAsset asset = new("setup.exe", new Uri("https://example.invalid/setup.exe"), Sha256Of("egal"u8.ToArray()));

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(asset, progress: null, CancellationToken.None);

        Assert.Equal(UpdateDownloadStatus.DownloadFailed, result.Status);
        Assert.Null(result.InstallerPath);
        Assert.Empty(Directory.GetFiles(_downloadDirectory));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_OnTransportFailure_ReportsDownloadFailed()
    {
        HttpUpdateInstaller sut = CreateSut(StubHttpMessageHandler.Throwing(new HttpRequestException("kein Netz")));
        UpdateAsset asset = new("setup.exe", new Uri("https://example.invalid/setup.exe"), Sha256Of("egal"u8.ToArray()));

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(asset, progress: null, CancellationToken.None);

        Assert.Equal(UpdateDownloadStatus.DownloadFailed, result.Status);
        Assert.Empty(Directory.GetFiles(_downloadDirectory));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WhenCancelled_ThrowsAndLeavesNoFile()
    {
        byte[] inhalt = Payload(512 * 1024);
        HttpUpdateInstaller sut = CreateSut(WithPayload(inhalt));
        UpdateAsset asset = new("setup.exe", new Uri("https://example.invalid/setup.exe"), Sha256Of(inhalt));
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.DownloadAndVerifyAsync(asset, progress: null, cts.Token));

        Assert.Empty(Directory.GetFiles(_downloadDirectory));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_ReportsProgressOnlyOnPercentChange()
    {
        // Mehr als hundert Puffer-Durchläufe: ohne die Entprellung käme jede Schreiboperation
        // als Meldung an, mit ihr höchstens ein Wert je Prozentschritt.
        byte[] inhalt = Payload(900 * 1024);
        HttpUpdateInstaller sut = CreateSut(WithPayload(inhalt));
        UpdateAsset asset = new("setup.exe", new Uri("https://example.invalid/setup.exe"), Sha256Of(inhalt));
        List<int> gemeldet = [];
        Progress<int> progress = new(gemeldet.Add);

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(asset, progress, CancellationToken.None);

        Assert.Equal(UpdateDownloadStatus.Verified, result.Status);
        // Progress<T> meldet über den Synchronisationskontext; kurz nachfassen, damit die
        // Rückrufe eingetroffen sind, bevor gezählt wird.
        await Task.Delay(50);
        Assert.NotEmpty(gemeldet);
        Assert.True(gemeldet.Count <= 101, $"Erwartet höchstens 101 Meldungen, waren {gemeldet.Count}.");
        Assert.Equal(gemeldet.Count, gemeldet.Distinct().Count());
        Assert.All(gemeldet, p => Assert.InRange(p, 0, 100));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WithoutContentLength_StillVerifies()
    {
        // Ohne Content-Length kann kein Prozentwert berechnet werden; der Download muss
        // trotzdem gelingen und die Prüfung greifen.
        byte[] inhalt = "Antwort ohne Längenangabe."u8.ToArray();
        StubHttpMessageHandler handler = StubHttpMessageHandler.WithStream(inhalt, setContentLength: false);
        HttpUpdateInstaller sut = CreateSut(handler);
        UpdateAsset asset = new("setup.exe", new Uri("https://example.invalid/setup.exe"), Sha256Of(inhalt));
        List<int> gemeldet = [];

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(asset, new Progress<int>(gemeldet.Add), CancellationToken.None);

        Assert.Equal(UpdateDownloadStatus.Verified, result.Status);
        Assert.Empty(gemeldet);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_UsesOnlyTheFileNamePartOfTheAsset()
    {
        // Ein Pfadanteil im Dateinamen darf nicht dazu führen, dass außerhalb des
        // Download-Verzeichnisses geschrieben wird.
        byte[] inhalt = "Pfadanteil im Namen."u8.ToArray();
        HttpUpdateInstaller sut = CreateSut(WithPayload(inhalt));
        UpdateAsset asset = new(Path.Combine("..", "..", "setup.exe"), new Uri("https://example.invalid/setup.exe"), Sha256Of(inhalt));

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(asset, progress: null, CancellationToken.None);

        Assert.Equal(UpdateDownloadStatus.Verified, result.Status);
        Assert.Equal(_downloadDirectory, Path.GetDirectoryName(result.InstallerPath));
    }

    [Fact]
    public void StartInstaller_WithMissingFile_ReturnsFalseInsteadOfThrowing()
    {
        HttpUpdateInstaller sut = CreateSut(StubHttpMessageHandler.WithStatus(HttpStatusCode.OK));
        string fehlt = Path.Combine(_downloadDirectory, "gibt-es-nicht.exe");

        bool gestartet = sut.StartInstaller(fehlt);

        Assert.False(gestartet);
    }

    [Fact]
    public void StartInstaller_WithoutPath_Throws()
    {
        HttpUpdateInstaller sut = CreateSut(StubHttpMessageHandler.WithStatus(HttpStatusCode.OK));

        _ = Assert.Throws<ArgumentException>(() => sut.StartInstaller("   "));
    }

    [Fact]
    public void Constructor_WithoutDownloadDirectory_Throws()
    {
        HttpClient client = CreateClient(StubHttpMessageHandler.WithStatus(HttpStatusCode.OK));

        _ = Assert.Throws<ArgumentException>(
            () => new HttpUpdateInstaller(client, "  ", NullLogger<HttpUpdateInstaller>.Instance));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WithoutAsset_Throws()
    {
        HttpUpdateInstaller sut = CreateSut(StubHttpMessageHandler.WithStatus(HttpStatusCode.OK));

        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.DownloadAndVerifyAsync(null!, progress: null, CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WhenTheDownloadFolderCannotBeCreated_ReportsStorageFailure()
    {
        // Der Ablageordner liegt unter %LOCALAPPDATA% und kann durch eine gleichnamige Datei
        // oder eine Richtlinie blockiert sein. Dann darf nichts geladen und erst recht nichts
        // ausgeführt werden — die Meldung muss den Grund benennen.
        string blockierterPfad = Path.Combine(Path.GetTempPath(), "MdExplorerInstallerTests-blockiert-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(blockierterPfad, "keine Mappe").ConfigureAwait(true);
        try
        {
            StubHttpMessageHandler handler = StubHttpMessageHandler.WithStatus(HttpStatusCode.OK);
            _verwerfbare.Add(handler);
            HttpClient client = new(handler);
            _verwerfbare.Add(client);
            HttpUpdateInstaller sut = new(client, blockierterPfad, NullLogger<HttpUpdateInstaller>.Instance);

            UpdateDownloadResult ergebnis = await sut.DownloadAndVerifyAsync(
                new UpdateAsset(
                    "MdExplorer-1.0.0-setup.exe",
                    new Uri("https://example.invalid/setup.exe"),
                    "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08"),
                progress: null,
                CancellationToken.None).ConfigureAwait(true);

            Assert.Equal(UpdateDownloadStatus.StorageFailed, ergebnis.Status);
            Assert.Null(ergebnis.InstallerPath);
        }
        finally
        {
            File.Delete(blockierterPfad);
        }
    }

    private HttpUpdateInstaller CreateSut(StubHttpMessageHandler handler)
    {
        _verwerfbare.Add(handler);
        HttpClient client = new(handler);
        _verwerfbare.Add(client);
        return new HttpUpdateInstaller(client, _downloadDirectory, NullLogger<HttpUpdateInstaller>.Instance);
    }

    private HttpClient CreateClient(StubHttpMessageHandler handler)
    {
        _verwerfbare.Add(handler);
        HttpClient client = new(handler);
        _verwerfbare.Add(client);
        return client;
    }

    private static StubHttpMessageHandler WithPayload(byte[] inhalt) =>
        StubHttpMessageHandler.WithStream(inhalt, setContentLength: true);

    /// <summary>
    /// Füllt den Puffer deterministisch. Ein Zufallsgenerator wäre hier fehl am Platz:
    /// Der Test soll bei jedem Lauf denselben Inhalt und damit denselben Hash prüfen.
    /// </summary>
    private static byte[] Payload(int length)
    {
        byte[] inhalt = new byte[length];
        for (int i = 0; i < length; i++)
        {
            inhalt[i] = (byte)(i % 251);
        }

        return inhalt;
    }

    private static string Sha256Of(byte[] inhalt) => Convert.ToHexString(SHA256.HashData(inhalt));
}
