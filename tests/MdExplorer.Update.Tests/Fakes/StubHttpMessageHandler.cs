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

    /// <summary>
    /// Erzeugt einen Handler mit frei gebauter Antwort: eigener Inhalt, eigene Kopfzeilen oder eine
    /// nachgestellte Weiterleitung über <see cref="HttpResponseMessage.RequestMessage"/>.
    /// </summary>
    public static StubHttpMessageHandler Responding(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(responder);

    /// <summary>Erzeugt einen Handler, der die angegebene Ausnahme wirft.</summary>
    public static StubHttpMessageHandler Throwing(Exception exception) =>
        new(_ => throw exception);

    /// <summary>
    /// Erzeugt einen Handler, der je nach Adresse unterschiedlich antwortet. Nötig, sobald ein
    /// Ablauf mehrere Ressourcen abruft, etwa erst das Release und dann dessen Prüfsummen-Datei.
    /// </summary>
    /// <param name="routes">
    /// Zuordnung von Adress-Bestandteil zu Antwort. Der erste Eintrag, dessen Schlüssel in der
    /// angefragten Adresse vorkommt, gewinnt.
    /// </param>
    /// <param name="fallback">Antwort, wenn keine Route greift.</param>
    public static StubHttpMessageHandler WithRoutes(
        IReadOnlyList<KeyValuePair<string, Func<HttpResponseMessage>>> routes,
        Func<HttpResponseMessage>? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(routes);

        return new StubHttpMessageHandler(request =>
        {
            string address = request.RequestUri?.AbsoluteUri ?? string.Empty;
            foreach (KeyValuePair<string, Func<HttpResponseMessage>> route in routes)
            {
                if (address.Contains(route.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return route.Value();
                }
            }

            return fallback?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        });
    }

    /// <summary>Kurzform für eine Route, die Text mit dem angegebenen Status liefert.</summary>
    public static Func<HttpResponseMessage> Text(string content, HttpStatusCode status = HttpStatusCode.OK) =>
        () => new HttpResponseMessage(status) { Content = new StringContent(content) };

    /// <summary>
    /// Erzeugt einen Handler, der die angegebenen Bytes als Datenstrom liefert.
    /// </summary>
    /// <param name="content">Der auszuliefernde Inhalt.</param>
    /// <param name="setContentLength">
    /// <see langword="false"/> unterdrückt die Längenangabe. Ohne sie kann der Aufrufer keinen
    /// Fortschritt in Prozent berechnen, ein Fall, der real vorkommt und getestet gehört.
    /// </param>
    public static StubHttpMessageHandler WithStream(byte[] content, bool setContentLength)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Der Lambda-Parameter heißt nicht '_': Innerhalb des Rumpfes wäre '_' sonst
        // der Parameter und keine Verwerfung, und die Zuweisung schlüge fehl.
        return new StubHttpMessageHandler(request =>
        {
            StreamContent streamContent = new(new MemoryStream(content, writable: false));
            if (!setContentLength)
            {
                _ = streamContent.Headers.Remove("Content-Length");
                streamContent.Headers.ContentLength = null;
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = streamContent };
        });
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;
        HttpResponseMessage response = _responder(request);
        // Echte Handler hängen die Anfrage an, die die Antwort geliefert hat; nach einer
        // Weiterleitung ist das die neue Adresse. Eine Antwort, die eine Weiterleitung
        // nachstellt, setzt sie selbst und behält sie.
        response.RequestMessage ??= request;
        return Task.FromResult(response);
    }
}
