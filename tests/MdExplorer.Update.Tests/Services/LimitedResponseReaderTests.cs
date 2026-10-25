using System.Net;
using MdExplorer.Update.Services;
using MdExplorer.Update.Tests.Fakes;

namespace MdExplorer.Update.Tests.Services;

/// <summary>
/// Kleine Antworten werden nur bis zur Grenze gelesen, gleich ob die Länge angekündigt ist, und
/// nur von zugelassenen Adressen nach allen Weiterleitungen.
/// </summary>
public sealed class LimitedResponseReaderTests : IDisposable
{
    private static readonly Uri Address = new("https://github.com/ReneSchustek/mdExplorer/releases/download/v1/a.sha256");

    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (IDisposable single in _disposables)
        {
            single.Dispose();
        }
    }

    [Fact]
    public async Task ReadAsync_WithinTheLimit_ReturnsTheBody()
    {
        HttpClient client = Client(StubHttpMessageHandler.WithStream("kurz"u8.ToArray(), setContentLength: true));

        byte[] body = await LimitedResponseReader.ReadAsync(client, Address, 16, ReleaseAddressPolicy.IsTransferAddress, TestContext.Current.CancellationToken);

        Assert.Equal("kurz"u8.ToArray(), body);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadAsync_OverTheLimit_Throws(bool announceLength)
    {
        HttpClient client = Client(StubHttpMessageHandler.WithStream(new byte[17], announceLength));

        HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(
            () => LimitedResponseReader.ReadAsync(client, Address, 16, isTrustedSource: null, TestContext.Current.CancellationToken));

        Assert.Equal(HttpRequestError.ConfigurationLimitExceeded, error.HttpRequestError);
    }

    [Fact]
    public async Task ReadAsync_AfterARedirectToAForeignHost_Throws()
    {
        HttpClient client = Client(StubHttpMessageHandler.Responding(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("x"u8.ToArray()),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, new Uri("https://example.invalid/a.sha256")),
        }));

        _ = await Assert.ThrowsAsync<HttpRequestException>(
            () => LimitedResponseReader.ReadAsync(client, Address, 16, ReleaseAddressPolicy.IsTransferAddress, TestContext.Current.CancellationToken));
    }

    private HttpClient Client(StubHttpMessageHandler handler)
    {
        _disposables.Add(handler);
        HttpClient client = new(handler, disposeHandler: false);
        _disposables.Add(client);
        return client;
    }
}
