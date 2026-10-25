using System.Net;
using MdExplorer.Update.Models;
using MdExplorer.Update.Options;
using MdExplorer.Update.Services;
using MdExplorer.Update.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.Update.Tests.Services;

/// <summary>
/// Prüft, wie der Checker das Installationspaket, dessen Prüfsumme und dessen Signatur aus einem
/// Release ermittelt. Prüfwert und Signatur entscheiden darüber, ob überhaupt installiert werden
/// darf; fehlt eines oder taugt es nicht, muss das Ergebnis „nicht installierbar" lauten statt zu raten.
/// </summary>
public sealed class GitHubUpdateCheckerAssetTests : IDisposable
{
    private const string ValidHash = "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08";
    private const string AssetBase = "https://github.com/ReneSchustek/mdExplorer/releases/download/v1.0.0/";

    // Der Prüfer liest die Signatur nur ein; ob sie stimmt, entscheidet erst der Installer.
    private static readonly byte[] SignatureBytes = [0x30, 0x44, 0x02, 0x20];

    private static readonly DateTimeOffset FixedNow = new(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri ApiBase = new UriBuilder("https", "api.github.com").Uri;

    private readonly FakeTimeProvider _time = new(FixedNow);
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (IDisposable single in _disposables)
        {
            single.Dispose();
        }
    }

    [Fact]
    public async Task CheckForUpdate_WithSetupAndChecksum_ReturnsInstallableAsset()
    {
        GitHubUpdateChecker checker = CreateChecker(ReleaseWithAssets(withChecksum: true, checksumContent: $"{ValidHash}  MdExplorer-1.0.0-setup.exe"));

        UpdateCheckResult result = await checker.CheckForUpdateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        UpdateAsset asset = Assert.IsType<UpdateAsset>(result.Asset);
        Assert.Equal("MdExplorer-1.0.0-setup.exe", asset.FileName, StringComparer.Ordinal);
        Assert.Equal(ValidHash, asset.ExpectedSha256, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(SignatureBytes, asset.Signature.ToArray());
        Assert.True(asset.IsVerifiable);
        Assert.True(result.IsInstallable);
    }

    [Fact]
    public async Task CheckForUpdate_WithoutSignatureAsset_IsNotInstallable()
    {
        GitHubUpdateChecker checker = CreateChecker(
            ReleaseWithAssets(withChecksum: true, checksumContent: $"{ValidHash}  MdExplorer-1.0.0-setup.exe", withSignature: false));

        UpdateCheckResult result = await checker.CheckForUpdateAsync(TestContext.Current.CancellationToken);

        UpdateAsset asset = Assert.IsType<UpdateAsset>(result.Asset);
        Assert.Equal(ValidHash, asset.ExpectedSha256, StringComparer.OrdinalIgnoreCase);
        Assert.True(asset.Signature.IsEmpty);
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public async Task CheckForUpdate_WithoutChecksumAsset_ReturnsAssetWithoutHash()
    {
        GitHubUpdateChecker checker = CreateChecker(ReleaseWithAssets(withChecksum: false));

        UpdateCheckResult result = await checker.CheckForUpdateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        UpdateAsset asset = Assert.IsType<UpdateAsset>(result.Asset);
        Assert.Null(asset.ExpectedSha256);
        Assert.False(asset.IsVerifiable);
        // Ohne Prüfwert darf nicht installiert werden; der Weg über die Release-Seite bleibt.
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public async Task CheckForUpdate_WithUnparsableChecksumFile_ReturnsAssetWithoutHash()
    {
        GitHubUpdateChecker checker = CreateChecker(ReleaseWithAssets(withChecksum: true, checksumContent: "das ist kein hexwert"));

        UpdateCheckResult result = await checker.CheckForUpdateAsync(TestContext.Current.CancellationToken);

        UpdateAsset asset = Assert.IsType<UpdateAsset>(result.Asset);
        Assert.Null(asset.ExpectedSha256);
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public async Task CheckForUpdate_WithTruncatedChecksum_ReturnsAssetWithoutHash()
    {
        // Zu kurzer Wert: sieht auf den ersten Blick nach Hex aus, ist aber kein SHA-256.
        GitHubUpdateChecker checker = CreateChecker(ReleaseWithAssets(withChecksum: true, checksumContent: "9F86D081"));

        UpdateCheckResult result = await checker.CheckForUpdateAsync(TestContext.Current.CancellationToken);

        UpdateAsset asset = Assert.IsType<UpdateAsset>(result.Asset);
        Assert.Null(asset.ExpectedSha256);
    }

    [Fact]
    public async Task CheckForUpdate_WhenChecksumDownloadFails_ReturnsAssetWithoutHash()
    {
        StubHttpMessageHandler handler = StubHttpMessageHandler.WithRoutes(
        [
            new("releases/latest", StubHttpMessageHandler.Text(ReleaseJson(withChecksum: true), HttpStatusCode.OK)),
            new(".sha256", StubHttpMessageHandler.Text(string.Empty, HttpStatusCode.InternalServerError)),
        ]);
        GitHubUpdateChecker checker = CreateChecker(handler);

        UpdateCheckResult result = await checker.CheckForUpdateAsync(TestContext.Current.CancellationToken);

        UpdateAsset asset = Assert.IsType<UpdateAsset>(result.Asset);
        Assert.Null(asset.ExpectedSha256);
    }

    [Fact]
    public async Task CheckForUpdate_WithoutSetupAsset_ReturnsNoAsset()
    {
        const string WithoutSetup = """
            {"tag_name":"v1.0.0","html_url":"https://github.com/ReneSchustek/mdExplorer/releases/tag/v1.0.0","assets":[
              {"name":"MdExplorer-1.0.0-win-x64.zip","browser_download_url":"https://github.com/ReneSchustek/mdExplorer/releases/download/v1.0.0/paket.zip"}
            ]}
            """;
        GitHubUpdateChecker checker = CreateChecker(StubHttpMessageHandler.WithJson(WithoutSetup));

        UpdateCheckResult result = await checker.CheckForUpdateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Null(result.Asset);
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public async Task CheckForUpdate_WithoutAssetsAtAll_ReturnsNoAsset()
    {
        const string WithoutAssets = """
            {"tag_name":"v1.0.0","html_url":"https://github.com/ReneSchustek/mdExplorer/releases/tag/v1.0.0"}
            """;
        GitHubUpdateChecker checker = CreateChecker(StubHttpMessageHandler.WithJson(WithoutAssets));

        UpdateCheckResult result = await checker.CheckForUpdateAsync(TestContext.Current.CancellationToken);

        Assert.Null(result.Asset);
    }

    [Fact]
    public async Task CheckForUpdate_WithChecksumFileContainingFileName_ReadsOnlyTheHash()
    {
        // Das übliche Format ist "<hex>  <dateiname>"; der Dateiname darf nicht mitgelesen werden.
        GitHubUpdateChecker checker = CreateChecker(
            ReleaseWithAssets(withChecksum: true, checksumContent: $"{ValidHash} *MdExplorer-1.0.0-setup.exe\n"));

        UpdateCheckResult result = await checker.CheckForUpdateAsync(TestContext.Current.CancellationToken);

        UpdateAsset asset = Assert.IsType<UpdateAsset>(result.Asset);
        Assert.Equal(ValidHash, asset.ExpectedSha256, StringComparer.OrdinalIgnoreCase);
    }

    private static string ReleaseJson(bool withChecksum, bool withSignature = true)
    {
        List<string> assets = [Asset("MdExplorer-1.0.0-setup.exe")];
        if (withChecksum)
        {
            assets.Add(Asset("MdExplorer-1.0.0-setup.exe.sha256"));
        }

        if (withSignature)
        {
            assets.Add(Asset("MdExplorer-1.0.0-setup.exe.sig"));
        }

        return "{\"tag_name\":\"v1.0.0\",\"html_url\":\"https://github.com/ReneSchustek/mdExplorer/releases/tag/v1.0.0\",\"assets\":["
            + string.Join(',', assets) + "]}";
    }

    private static string Asset(string name) =>
        "{\"name\":\"" + name + "\",\"browser_download_url\":\"" + AssetBase + name + "\"}";

    private static StubHttpMessageHandler ReleaseWithAssets(bool withChecksum, string checksumContent = "", bool withSignature = true) =>
        StubHttpMessageHandler.WithRoutes(
        [
            new("releases/latest", StubHttpMessageHandler.Text(ReleaseJson(withChecksum, withSignature))),
            new(".sha256", StubHttpMessageHandler.Text(checksumContent)),
            new(".sig", () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(SignatureBytes) }),
        ]);

    private GitHubUpdateChecker CreateChecker(StubHttpMessageHandler handler)
    {
        _disposables.Add(handler);
        HttpClient client = new(handler, disposeHandler: false) { BaseAddress = ApiBase };
        _disposables.Add(client);
        return new GitHubUpdateChecker(
            client,
            Microsoft.Extensions.Options.Options.Create(new UpdateOptions()),
            new FakeAppVersionProvider(new SemanticVersion(0, 9, 0)),
            new FakeUpdateCheckJournal(),
            _time,
            NullLogger<GitHubUpdateChecker>.Instance);
    }
}
