using System.Net;
using MdExplorer.Update.Models;
using MdExplorer.Update.Options;
using MdExplorer.Update.Services;
using MdExplorer.Update.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.Update.Tests.Services;

/// <summary>
/// Die Release-Antwort ist Eingabe von außen. Der Prüfer übernimmt daraus nur Adressen des eigenen
/// Repositorys über https, liest Begleitdateien nur bis zu einer festen Größe und lässt sich weder
/// von einer falsch gehenden Uhr noch von der Drossel aussperren, wenn der Nutzer selbst prüft.
/// </summary>
public sealed class GitHubUpdateCheckerSecurityTests : IDisposable
{
    private const string ValidHash = "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08";
    private const string AssetBase = "https://github.com/ReneSchustek/mdExplorer/releases/download/v1.0.0/";

    private static readonly DateTimeOffset Now = new(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri ApiBase = new UriBuilder("https", "api.github.com").Uri;

    private readonly FakeTimeProvider _time = new(Now);
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (IDisposable single in _disposables)
        {
            single.Dispose();
        }
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("http://github.com/ReneSchustek/mdExplorer/releases/tag/v1.0.0")]
    [InlineData("https://example.invalid/ReneSchustek/mdExplorer/releases/tag/v1.0.0")]
    [InlineData("https://github.com/Fremder/mdExplorer/releases/tag/v1.0.0")]
    public async Task CheckForUpdate_WithAForeignReleasePage_FallsBackToTheOwnReleasesPage(string releasePage)
    {
        GitHubUpdateChecker checker = CreateChecker(Release(releasePage, AssetBase + "MdExplorer-1.0.0-setup.exe"));

        UpdateCheckResult result = await checker.CheckForUpdateAsync(TestContext.Current.CancellationToken);

        Assert.Equal("https://github.com/ReneSchustek/mdExplorer/releases/latest", result.ReleaseUrl?.AbsoluteUri);
    }

    [Theory]
    [InlineData("http://github.com/ReneSchustek/mdExplorer/releases/download/v1.0.0/MdExplorer-1.0.0-setup.exe")]
    [InlineData("https://example.invalid/MdExplorer-1.0.0-setup.exe")]
    [InlineData("https://github.com/Fremder/mdExplorer/releases/download/v1.0.0/MdExplorer-1.0.0-setup.exe")]
    [InlineData("https://github.com/ReneSchustek/mdExplorer/releases/download/../../../Fremder/x/MdExplorer-1.0.0-setup.exe")]
    public async Task CheckForUpdate_WithAForeignPackageAddress_OffersNoPackage(string packageAddress)
    {
        GitHubUpdateChecker checker = CreateChecker(Release(OwnReleasePage, packageAddress));

        UpdateCheckResult result = await checker.CheckForUpdateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Null(result.Asset);
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public async Task CheckForUpdate_WithAnOversizedChecksumFile_TakesNoChecksum()
    {
        // Ein gültiger Prüfwert am Anfang, danach ein Megabyte Füllung: gelesen werden darf das nicht.
        string oversized = ValidHash + "  MdExplorer-1.0.0-setup.exe\n" + new string(' ', 1024 * 1024);
        GitHubUpdateChecker checker = CreateChecker(Release(OwnReleasePage, AssetBase + "MdExplorer-1.0.0-setup.exe", oversized));

        UpdateCheckResult result = await checker.CheckForUpdateAsync(TestContext.Current.CancellationToken);

        UpdateAsset asset = Assert.IsType<UpdateAsset>(result.Asset);
        Assert.Null(asset.ExpectedSha256);
    }

    [Fact]
    public async Task CheckForUpdate_WithALastCheckInTheFuture_ChecksAnyway()
    {
        // Die Uhr ging einmal einen Monat vor. Ohne Gegenwehr fände bis dahin keine Prüfung statt.
        FakeUpdateCheckJournal journal = new(Now.AddDays(30));
        GitHubUpdateChecker checker = CreateChecker(Release(OwnReleasePage, AssetBase + "MdExplorer-1.0.0-setup.exe"), journal);

        UpdateCheckResult result = await checker.CheckForUpdateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(Now, journal.LastCheck);
    }

    [Fact]
    public async Task CheckForUpdate_WhenForcedWithinTheInterval_ChecksAnyway()
    {
        FakeUpdateCheckJournal journal = new(Now.AddHours(-1));
        GitHubUpdateChecker checker = CreateChecker(Release(OwnReleasePage, AssetBase + "MdExplorer-1.0.0-setup.exe"), journal);

        UpdateCheckResult result = await checker.CheckForUpdateAsync(force: true, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(1, journal.WriteCount);
    }

    [Fact]
    public async Task CheckForUpdate_WithAnOversizedReleaseResponse_Fails()
    {
        string oversized = """{"tag_name":"v1.0.0","body":" """ + new string('x', 2 * 1024 * 1024) + "\"}";
        StubHttpMessageHandler handler = StubHttpMessageHandler.WithJson(oversized);
        GitHubUpdateChecker checker = CreateChecker(handler);

        UpdateCheckResult result = await checker.CheckForUpdateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    private const string OwnReleasePage = "https://github.com/ReneSchustek/mdExplorer/releases/tag/v1.0.0";

    private static StubHttpMessageHandler Release(string htmlUrl, string setupUrl, string? checksum = null)
    {
        string json = $$"""
            {"tag_name":"v1.0.0","html_url":"{{htmlUrl}}","assets":[
              {"name":"MdExplorer-1.0.0-setup.exe","browser_download_url":"{{setupUrl}}"},
              {"name":"MdExplorer-1.0.0-setup.exe.sha256","browser_download_url":"{{AssetBase}}MdExplorer-1.0.0-setup.exe.sha256"}
            ]}
            """;
        return StubHttpMessageHandler.WithRoutes(
        [
            new("releases/latest", StubHttpMessageHandler.Text(json)),
            new(".sha256", StubHttpMessageHandler.Text(checksum ?? ValidHash + "  MdExplorer-1.0.0-setup.exe")),
        ]);
    }

    private GitHubUpdateChecker CreateChecker(StubHttpMessageHandler handler, FakeUpdateCheckJournal? journal = null)
    {
        _disposables.Add(handler);
        HttpClient client = new(handler, disposeHandler: false) { BaseAddress = ApiBase };
        _disposables.Add(client);
        return new GitHubUpdateChecker(
            client,
            Microsoft.Extensions.Options.Options.Create(new UpdateOptions()),
            new FakeAppVersionProvider(new SemanticVersion(0, 9, 0)),
            journal ?? new FakeUpdateCheckJournal(),
            _time,
            NullLogger<GitHubUpdateChecker>.Instance);
    }
}
