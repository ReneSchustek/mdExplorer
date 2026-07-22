using System.Net;
using MdExplorer.Update.Models;
using MdExplorer.Update.Options;
using MdExplorer.Update.Services;
using MdExplorer.Update.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.Update.Tests.Services;

/// <summary>Tests für <see cref="GitHubUpdateChecker"/> — Vergleich, Throttle und Fehlertoleranz.</summary>
public sealed class GitHubUpdateCheckerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri ApiBase = new UriBuilder("https", "api.github.com").Uri;

    private readonly FakeTimeProvider _time = new(Now);

    [Fact]
    public async Task CheckForUpdate_WhenRemoteIsNewer_ReturnsAvailableAndPersistsTimestamp()
    {
        const string Json = """
            {"tag_name":"v1.0.0","html_url":"https://github.com/ReneSchustek/mdExplorer/releases/tag/v1.0.0"}
            """;
        using StubHttpMessageHandler handler = StubHttpMessageHandler.WithJson(Json);
        using HttpClient client = new(handler, disposeHandler: false) { BaseAddress = ApiBase };
        FakeUpdateCheckJournal journal = new();
        GitHubUpdateChecker checker = CreateChecker(client, new SemanticVersion(0, 9, 0), journal);

        UpdateCheckResult result = await checker.CheckForUpdateAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(new SemanticVersion(1, 0, 0), result.LatestVersion);
        Assert.Equal("https://github.com/ReneSchustek/mdExplorer/releases/tag/v1.0.0", result.ReleaseUrl?.AbsoluteUri);
        Assert.Equal(Now, journal.LastCheck);
        Assert.Equal(1, journal.WriteCount);
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("v0.5.0")]
    public async Task CheckForUpdate_WhenRemoteIsNotNewer_ReturnsUpToDate(string remoteTag)
    {
        string json = $$"""{"tag_name":"{{remoteTag}}","html_url":"https://example.test/r"}""";
        using StubHttpMessageHandler handler = StubHttpMessageHandler.WithJson(json);
        using HttpClient client = new(handler, disposeHandler: false) { BaseAddress = ApiBase };
        GitHubUpdateChecker checker = CreateChecker(client, new SemanticVersion(1, 0, 0), new FakeUpdateCheckJournal());

        UpdateCheckResult result = await checker.CheckForUpdateAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdate_WhenWithinInterval_SkipsWithoutNetworkCall()
    {
        using StubHttpMessageHandler handler = StubHttpMessageHandler.WithJson("""{"tag_name":"v9.9.9"}""");
        using HttpClient client = new(handler, disposeHandler: false) { BaseAddress = ApiBase };
        FakeUpdateCheckJournal journal = new(Now.AddHours(-1));
        GitHubUpdateChecker checker = CreateChecker(client, new SemanticVersion(0, 9, 0), journal);

        UpdateCheckResult result = await checker.CheckForUpdateAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Skipped, result.Status);
        Assert.Null(handler.LastRequestUri);
        Assert.Equal(0, journal.WriteCount);
    }

    [Fact]
    public async Task CheckForUpdate_WhenLastCheckOlderThanInterval_PerformsCheck()
    {
        using StubHttpMessageHandler handler = StubHttpMessageHandler.WithJson("""{"tag_name":"v2.0.0","html_url":"https://example.test/r"}""");
        using HttpClient client = new(handler, disposeHandler: false) { BaseAddress = ApiBase };
        FakeUpdateCheckJournal journal = new(Now.AddHours(-25));
        GitHubUpdateChecker checker = CreateChecker(client, new SemanticVersion(1, 0, 0), journal);

        UpdateCheckResult result = await checker.CheckForUpdateAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.NotNull(handler.LastRequestUri);
    }

    [Fact]
    public async Task CheckForUpdate_WhenNetworkFails_ReturnsFailedWithoutPersisting()
    {
        using StubHttpMessageHandler handler = StubHttpMessageHandler.Throwing(new HttpRequestException("kein Netz"));
        using HttpClient client = new(handler, disposeHandler: false) { BaseAddress = ApiBase };
        FakeUpdateCheckJournal journal = new();
        GitHubUpdateChecker checker = CreateChecker(client, new SemanticVersion(0, 9, 0), journal);

        UpdateCheckResult result = await checker.CheckForUpdateAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Equal(0, journal.WriteCount);
    }

    [Fact]
    public async Task CheckForUpdate_WhenServerReturnsError_ReturnsFailed()
    {
        using StubHttpMessageHandler handler = StubHttpMessageHandler.WithStatus(HttpStatusCode.NotFound);
        using HttpClient client = new(handler, disposeHandler: false) { BaseAddress = ApiBase };
        GitHubUpdateChecker checker = CreateChecker(client, new SemanticVersion(0, 9, 0), new FakeUpdateCheckJournal());

        UpdateCheckResult result = await checker.CheckForUpdateAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task CheckForUpdate_WhenTagIsUnparsable_ReturnsFailed()
    {
        using StubHttpMessageHandler handler = StubHttpMessageHandler.WithJson("""{"tag_name":"nightly-build","html_url":"https://example.test/r"}""");
        using HttpClient client = new(handler, disposeHandler: false) { BaseAddress = ApiBase };
        GitHubUpdateChecker checker = CreateChecker(client, new SemanticVersion(0, 9, 0), new FakeUpdateCheckJournal());

        UpdateCheckResult result = await checker.CheckForUpdateAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task CheckForUpdate_WhenHtmlUrlMissing_FallsBackToReleasesPage()
    {
        using StubHttpMessageHandler handler = StubHttpMessageHandler.WithJson("""{"tag_name":"v1.0.0"}""");
        using HttpClient client = new(handler, disposeHandler: false) { BaseAddress = ApiBase };
        GitHubUpdateChecker checker = CreateChecker(client, new SemanticVersion(0, 9, 0), new FakeUpdateCheckJournal());

        UpdateCheckResult result = await checker.CheckForUpdateAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("https://github.com/ReneSchustek/mdExplorer/releases/latest", result.ReleaseUrl?.AbsoluteUri);
    }

    private GitHubUpdateChecker CreateChecker(
        HttpClient client,
        SemanticVersion currentVersion,
        FakeUpdateCheckJournal journal) =>
        new(
            client,
            Microsoft.Extensions.Options.Options.Create(new UpdateOptions()),
            new FakeAppVersionProvider(currentVersion),
            journal,
            _time,
            NullLogger<GitHubUpdateChecker>.Instance);
}
