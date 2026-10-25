using System.ComponentModel;
using System.Net;
using MdExplorer.Update.Models;
using MdExplorer.Update.Services;
using MdExplorer.Update.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.Update.Tests.Services;

/// <summary>
/// Gestartet wird nur das Paket, das derselbe Installer gerade geprüft hat, und nur, solange es
/// unverändert ist. Der Ablageordner ist für jedes Programm des Nutzers beschreibbar; zwischen
/// Prüfung und Start könnte dort sonst eine andere Datei liegen.
/// </summary>
public sealed class HttpUpdateInstallerStartTests : IDisposable
{
    private readonly InstallerTestBench _bench = new();

    public void Dispose() => _bench.Dispose();

    [Fact]
    public async Task StartInstaller_AfterVerification_LaunchesThatFile()
    {
        (HttpUpdateInstaller sut, string path) = await DownloadVerifiedAsync();

        bool started = sut.StartInstaller(path);

        Assert.True(started);
        Assert.Equal([path], _bench.Launched);
    }

    [Fact]
    public async Task StartInstaller_WithAPathThatWasNotVerified_DoesNotLaunch()
    {
        // Ein geprüftes Paket liegt bereit; gestartet werden soll aber eine andere Datei.
        (HttpUpdateInstaller sut, _) = await DownloadVerifiedAsync();
        string somewhere = Path.Combine(_bench.DownloadDirectory, "irgendein.exe");
        await File.WriteAllBytesAsync(somewhere, "fremd"u8.ToArray(), TestContext.Current.CancellationToken);

        bool started = sut.StartInstaller(somewhere);

        Assert.False(started);
        Assert.Empty(_bench.Launched);
    }

    [Fact]
    public async Task StartInstaller_WhenTheFileChangedAfterVerification_DoesNotLaunch()
    {
        (HttpUpdateInstaller sut, string path) = await DownloadVerifiedAsync();
        await File.WriteAllBytesAsync(path, "ausgetauscht"u8.ToArray(), TestContext.Current.CancellationToken);

        bool started = sut.StartInstaller(path);

        Assert.False(started);
        Assert.Empty(_bench.Launched);
    }

    [Fact]
    public async Task StartInstaller_WhenTheFileIsGone_ReturnsFalseInsteadOfThrowing()
    {
        (HttpUpdateInstaller sut, string path) = await DownloadVerifiedAsync();
        File.Delete(path);

        bool started = sut.StartInstaller(path);

        Assert.False(started);
    }

    [Fact]
    public async Task StartInstaller_WhenTheLaunchIsRefused_ReturnsFalse()
    {
        // SmartScreen oder die Benutzerkontensteuerung brechen ab: Die Anwendung läuft weiter.
        (HttpUpdateInstaller sut, string path) = await DownloadVerifiedAsync();
        _bench.LaunchFailure = new Win32Exception(1223, "Vom Benutzer abgebrochen.");

        bool started = sut.StartInstaller(path);

        Assert.False(started);
    }

    [Fact]
    public void StartInstaller_BeforeAnyVerification_DoesNotLaunch()
    {
        HttpUpdateInstaller sut = _bench.CreateSut(StubHttpMessageHandler.WithStatus(HttpStatusCode.OK));

        bool started = sut.StartInstaller(Path.Combine(_bench.DownloadDirectory, "irgendein.exe"));

        Assert.False(started);
        Assert.Empty(_bench.Launched);
    }

    [Fact]
    public void StartInstaller_WithoutPath_Throws()
    {
        HttpUpdateInstaller sut = _bench.CreateSut(StubHttpMessageHandler.WithStatus(HttpStatusCode.OK));

        _ = Assert.Throws<ArgumentException>(() => sut.StartInstaller("   "));
    }

    [Fact]
    public void Constructor_WithoutDownloadDirectory_Throws()
    {
        using HttpClient client = new();

        _ = Assert.Throws<ArgumentException>(
            () => new HttpUpdateInstaller(client, "  ", _bench.Time, NullLogger<HttpUpdateInstaller>.Instance));
    }

    private async Task<(HttpUpdateInstaller Sut, string Path)> DownloadVerifiedAsync()
    {
        byte[] content = "Ein geprüftes Paket."u8.ToArray();
        HttpUpdateInstaller sut = _bench.CreateSut(InstallerTestBench.WithPayload(content));
        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(_bench.AssetFor(content), progress: null, TestContext.Current.CancellationToken).ConfigureAwait(true);
        return (sut, Assert.IsType<string>(result.InstallerPath));
    }
}
