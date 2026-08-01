using System.Net;
using MdExplorer.Update.Models;
using MdExplorer.Update.Options;
using MdExplorer.Update.Services;
using MdExplorer.Update.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.Update.Tests.Services;

/// <summary>
/// Prüft, wie der Checker das Installationspaket und dessen Prüfsumme aus einem Release
/// ermittelt. Der Prüfwert entscheidet darüber, ob überhaupt installiert werden darf —
/// fehlt oder taugt er nicht, muss das Ergebnis „nicht installierbar" lauten statt zu raten.
/// </summary>
public sealed class GitHubUpdateCheckerAssetTests : IDisposable
{
    private const string GueltigerHash = "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08";

    private static readonly DateTimeOffset Jetzt = new(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri ApiBasis = new UriBuilder("https", "api.github.com").Uri;

    private readonly FakeTimeProvider _zeit = new(Jetzt);
    private readonly List<IDisposable> _verwerfbare = [];

    public void Dispose()
    {
        foreach (IDisposable einzelnes in _verwerfbare)
        {
            einzelnes.Dispose();
        }
    }

    [Fact]
    public async Task CheckForUpdate_WithSetupAndChecksum_ReturnsInstallableAsset()
    {
        GitHubUpdateChecker checker = CreateChecker(ReleaseMitAssets(mitPruefsumme: true, pruefsummenInhalt: $"{GueltigerHash}  MdExplorer-1.0.0-setup.exe"));

        UpdateCheckResult result = await checker.CheckForUpdateAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.NotNull(result.Asset);
        Assert.Equal("MdExplorer-1.0.0-setup.exe", result.Asset!.FileName, StringComparer.Ordinal);
        Assert.Equal(GueltigerHash, result.Asset.ExpectedSha256, StringComparer.OrdinalIgnoreCase);
        Assert.True(result.Asset.IsVerifiable);
        Assert.True(result.IsInstallable);
    }

    [Fact]
    public async Task CheckForUpdate_WithoutChecksumAsset_ReturnsAssetWithoutHash()
    {
        GitHubUpdateChecker checker = CreateChecker(ReleaseMitAssets(mitPruefsumme: false));

        UpdateCheckResult result = await checker.CheckForUpdateAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.NotNull(result.Asset);
        Assert.Null(result.Asset!.ExpectedSha256);
        Assert.False(result.Asset.IsVerifiable);
        // Ohne Prüfwert darf nicht installiert werden — der Weg über die Release-Seite bleibt.
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public async Task CheckForUpdate_WithUnparsableChecksumFile_ReturnsAssetWithoutHash()
    {
        GitHubUpdateChecker checker = CreateChecker(ReleaseMitAssets(mitPruefsumme: true, pruefsummenInhalt: "das ist kein hexwert"));

        UpdateCheckResult result = await checker.CheckForUpdateAsync(CancellationToken.None);

        Assert.NotNull(result.Asset);
        Assert.Null(result.Asset!.ExpectedSha256);
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public async Task CheckForUpdate_WithTruncatedChecksum_ReturnsAssetWithoutHash()
    {
        // Zu kurzer Wert: sieht auf den ersten Blick nach Hex aus, ist aber kein SHA-256.
        GitHubUpdateChecker checker = CreateChecker(ReleaseMitAssets(mitPruefsumme: true, pruefsummenInhalt: "9F86D081"));

        UpdateCheckResult result = await checker.CheckForUpdateAsync(CancellationToken.None);

        Assert.Null(result.Asset!.ExpectedSha256);
    }

    [Fact]
    public async Task CheckForUpdate_WhenChecksumDownloadFails_ReturnsAssetWithoutHash()
    {
        StubHttpMessageHandler handler = StubHttpMessageHandler.WithRoutes(
        [
            new("releases/latest", StubHttpMessageHandler.Text(ReleaseJson(mitPruefsumme: true), HttpStatusCode.OK)),
            new(".sha256", StubHttpMessageHandler.Text(string.Empty, HttpStatusCode.InternalServerError)),
        ]);
        GitHubUpdateChecker checker = CreateChecker(handler);

        UpdateCheckResult result = await checker.CheckForUpdateAsync(CancellationToken.None);

        Assert.NotNull(result.Asset);
        Assert.Null(result.Asset!.ExpectedSha256);
    }

    [Fact]
    public async Task CheckForUpdate_WithoutSetupAsset_ReturnsNoAsset()
    {
        const string OhneSetup = """
            {"tag_name":"v1.0.0","html_url":"https://example.invalid/r","assets":[
              {"name":"MdExplorer-1.0.0-win-x64.zip","browser_download_url":"https://example.invalid/paket.zip"}
            ]}
            """;
        GitHubUpdateChecker checker = CreateChecker(StubHttpMessageHandler.WithJson(OhneSetup));

        UpdateCheckResult result = await checker.CheckForUpdateAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Null(result.Asset);
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public async Task CheckForUpdate_WithoutAssetsAtAll_ReturnsNoAsset()
    {
        const string OhneAssets = """
            {"tag_name":"v1.0.0","html_url":"https://example.invalid/r"}
            """;
        GitHubUpdateChecker checker = CreateChecker(StubHttpMessageHandler.WithJson(OhneAssets));

        UpdateCheckResult result = await checker.CheckForUpdateAsync(CancellationToken.None);

        Assert.Null(result.Asset);
    }

    [Fact]
    public async Task CheckForUpdate_WithChecksumFileContainingFileName_ReadsOnlyTheHash()
    {
        // Das übliche Format ist "<hex>  <dateiname>" — der Dateiname darf nicht mitgelesen werden.
        GitHubUpdateChecker checker = CreateChecker(
            ReleaseMitAssets(mitPruefsumme: true, pruefsummenInhalt: $"{GueltigerHash} *MdExplorer-1.0.0-setup.exe\n"));

        UpdateCheckResult result = await checker.CheckForUpdateAsync(CancellationToken.None);

        Assert.Equal(GueltigerHash, result.Asset!.ExpectedSha256, StringComparer.OrdinalIgnoreCase);
    }

    private static string ReleaseJson(bool mitPruefsumme)
    {
        const string SetupAsset =
            "{\"name\":\"MdExplorer-1.0.0-setup.exe\",\"browser_download_url\":\"https://example.invalid/setup.exe\"}";
        const string PruefsummenAsset =
            "{\"name\":\"MdExplorer-1.0.0-setup.exe.sha256\",\"browser_download_url\":\"https://example.invalid/setup.exe.sha256\"}";

        string assets = mitPruefsumme ? SetupAsset + "," + PruefsummenAsset : SetupAsset;
        return "{\"tag_name\":\"v1.0.0\",\"html_url\":\"https://example.invalid/r\",\"assets\":[" + assets + "]}";
    }

    private static StubHttpMessageHandler ReleaseMitAssets(bool mitPruefsumme, string pruefsummenInhalt = "") =>
        StubHttpMessageHandler.WithRoutes(
        [
            new("releases/latest", StubHttpMessageHandler.Text(ReleaseJson(mitPruefsumme))),
            new(".sha256", StubHttpMessageHandler.Text(pruefsummenInhalt)),
        ]);

    private GitHubUpdateChecker CreateChecker(StubHttpMessageHandler handler)
    {
        _verwerfbare.Add(handler);
        HttpClient client = new(handler, disposeHandler: false) { BaseAddress = ApiBasis };
        _verwerfbare.Add(client);
        return new GitHubUpdateChecker(
            client,
            Microsoft.Extensions.Options.Options.Create(new UpdateOptions()),
            new FakeAppVersionProvider(new SemanticVersion(0, 9, 0)),
            new FakeUpdateCheckJournal(),
            _zeit,
            NullLogger<GitHubUpdateChecker>.Instance);
    }
}
