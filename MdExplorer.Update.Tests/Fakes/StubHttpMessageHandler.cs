using System.Net;

namespace MdExplorer.Update.Tests.Fakes;

/// <summary>
/// Test-Handler, der eine vorgegebene Antwort liefert oder eine vorgegebene Ausnahme wirft.
/// Erlaubt das deterministische Testen des <c>GitHubUpdateChecker</c> ohne echtes Netz.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    private StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = responder;

    /// <summary>Die zuletzt gesendete Anfrage-URI (relativ aufgelöst gegen die Basis-Adresse).</summary>
    public Uri? LastRequestUri { get; private set; }

    /// <summary>Erzeugt einen Handler, der mit dem angegebenen JSON-Body und Status antwortet.</summary>
    public static StubHttpMessageHandler WithJson(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });

    /// <summary>Erzeugt einen Handler, der nur den angegebenen Status (ohne Body) liefert.</summary>
    public static StubHttpMessageHandler WithStatus(HttpStatusCode statusCode) =>
        new(_ => new HttpResponseMessage(statusCode));

    /// <summary>Erzeugt einen Handler, der die angegebene Ausnahme wirft.</summary>
    public static StubHttpMessageHandler Throwing(Exception exception) =>
        new(_ => throw exception);

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;
        return Task.FromResult(_responder(request));
    }
}
