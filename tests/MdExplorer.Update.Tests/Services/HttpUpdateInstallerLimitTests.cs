using System.Net;
using MdExplorer.Update.Models;
using MdExplorer.Update.Services;
using MdExplorer.Update.Tests.Fakes;

namespace MdExplorer.Update.Tests.Services;

/// <summary>
/// Der Download folgt nur den Release-Adressen, bricht über der Höchstgröße und bei einer
/// stehenden Verbindung ab und erkennt einen Körper, der vor der angekündigten Länge endet.
/// Ohne diese Grenzen hinge der Dialog unbegrenzt, oder ein endloser Strom füllte die Platte.
/// </summary>
public sealed class HttpUpdateInstallerLimitTests : IDisposable
{
    private static readonly DownloadLimits SmallLimits = new(1024, TimeSpan.FromSeconds(30));

    private readonly InstallerTestBench _bench = new();

    public void Dispose() => _bench.Dispose();

    [Theory]
    [InlineData("http://github.com/ReneSchustek/mdExplorer/releases/download/v1.0.0/MdExplorer-1.0.0-setup.exe")]
    [InlineData("https://example.invalid/MdExplorer-1.0.0-setup.exe")]
    [InlineData("file:///C:/Temp/MdExplorer-1.0.0-setup.exe")]
    public async Task DownloadAndVerifyAsync_FromAForeignAddress_DoesNotDownload(string address)
    {
        byte[] content = "Von woanders."u8.ToArray();
        StubHttpMessageHandler handler = InstallerTestBench.WithPayload(content);
        HttpUpdateInstaller sut = _bench.CreateSut(handler);
        UpdateAsset asset = _bench.AssetFor(content) with { DownloadUrl = new Uri(address) };

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(asset, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.DownloadFailed, result.Status);
        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WithANameThatIsNoProgram_DoesNotDownload()
    {
        byte[] content = "Ein Skript statt eines Setups."u8.ToArray();
        StubHttpMessageHandler handler = InstallerTestBench.WithPayload(content);
        HttpUpdateInstaller sut = _bench.CreateSut(handler);
        UpdateAsset asset = _bench.AssetFor(content) with { FileName = "MdExplorer-1.0.0-setup.cmd" };

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(asset, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.DownloadFailed, result.Status);
        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WhenRedirectedToAForeignHost_LeavesNoFile()
    {
        byte[] content = "Weitergeleitet."u8.ToArray();
        StubHttpMessageHandler handler = StubHttpMessageHandler.Responding(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, new Uri("https://example.invalid/setup.exe")),
        });
        HttpUpdateInstaller sut = _bench.CreateSut(handler);

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(_bench.AssetFor(content), progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.DownloadFailed, result.Status);
        Assert.Empty(_bench.StoredFiles());
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WhenRedirectedToTheReleaseStorage_Verifies()
    {
        byte[] content = "Vom Speicher-Host."u8.ToArray();
        StubHttpMessageHandler handler = StubHttpMessageHandler.Responding(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, new Uri("https://release-assets.githubusercontent.com/github-production-release-asset/1")),
        });
        HttpUpdateInstaller sut = _bench.CreateSut(handler);

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(_bench.AssetFor(content), progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.Verified, result.Status);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WhenTheAnnouncedLengthExceedsTheLimit_ReportsDownloadFailed()
    {
        byte[] content = InstallerTestBench.Payload(2048);
        HttpUpdateInstaller sut = _bench.CreateSut(InstallerTestBench.WithPayload(content), SmallLimits);

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(_bench.AssetFor(content), progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.DownloadFailed, result.Status);
        Assert.Empty(_bench.StoredFiles());
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WhenAStreamWithoutLengthExceedsTheLimit_ReportsDownloadFailed()
    {
        byte[] content = InstallerTestBench.Payload(2048);
        HttpUpdateInstaller sut = _bench.CreateSut(StubHttpMessageHandler.WithStream(content, setContentLength: false), SmallLimits);

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(_bench.AssetFor(content), progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.DownloadFailed, result.Status);
        Assert.Empty(_bench.StoredFiles());
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WhenTheBodyEndsBeforeTheAnnouncedLength_ReportsDownloadFailed()
    {
        byte[] content = "Zu kurz."u8.ToArray();
        HttpUpdateInstaller sut = _bench.CreateSut(InstallerTestBench.WithContent(() =>
        {
            ByteArrayContent truncated = new(content);
            truncated.Headers.ContentLength = content.Length + 100;
            return truncated;
        }));

        UpdateDownloadResult result = await sut.DownloadAndVerifyAsync(_bench.AssetFor(content), progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateDownloadStatus.DownloadFailed, result.Status);
        Assert.Empty(_bench.StoredFiles());
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_WhenTheConnectionStalls_ReportsDownloadFailedAfterTheStallTimeout()
    {
        StallingStream body = new();
        _bench.Track(body);
        HttpUpdateInstaller sut = _bench.CreateSut(InstallerTestBench.WithContent(() => new StreamContent(body)), SmallLimits);

        Task<UpdateDownloadResult> download = sut.DownloadAndVerifyAsync(_bench.AssetFor("nie geliefert"u8.ToArray()), progress: null, TestContext.Current.CancellationToken);
        await body.ReadStarted.WaitAsync(TestContext.Current.CancellationToken);
        bool finishedBeforeTimeout = download.IsCompleted;
        _bench.Time.Advance(SmallLimits.StallTimeout);
        UpdateDownloadResult result = await download.ConfigureAwait(true);

        Assert.False(finishedBeforeTimeout);
        Assert.Equal(UpdateDownloadStatus.DownloadFailed, result.Status);
        Assert.Empty(_bench.StoredFiles());
    }

    /// <summary>Ein Strom, der nie Daten liefert und erst auf den Abbruch hin endet.</summary>
    private sealed class StallingStream : MemoryStream
    {
        private readonly TaskCompletionSource _readStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReadStarted => _readStarted.Task;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _ = _readStarted.TrySetResult();
            await new TaskCompletionSource().Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }
    }
}
