namespace MdExplorer.App.Services;

/// <summary>Was die Vorschau mit einer Navigation tut.</summary>
internal enum PreviewNavigationKind
{
    /// <summary>Das eigene Dokument wird geladen und bleibt in der Fläche.</summary>
    Document,

    /// <summary>Ein WikiLink; die Anwendung öffnet die Zieldatei.</summary>
    WikiLink,

    /// <summary>Ein Verweis ins Netz oder an das Mailprogramm; er geht an das Standardprogramm.</summary>
    External,

    /// <summary>Alles Übrige; die Navigation wird verworfen.</summary>
    Blocked,
}

/// <summary>
/// Entscheidet, wohin eine Navigation aus der Vorschau führt.
/// </summary>
/// <remarks>
/// <para>
/// In der Fläche bleibt nur das Dokument, das die Anwendung selbst per <c>NavigateToString</c>
/// lädt. Ein Verweis ins Netz geht an den Standardbrowser: Die eingebettete Fläche hat keine
/// Adressleiste und nicht die Vorkehrungen eines Browsers, eine fremde Seite darin sähe aus wie
/// ein Teil der Anwendung.
/// </para>
/// <para>
/// Weitergereicht wird nur, was der Nutzer selbst angeklickt hat. Eine Weiterleitung im Dokument
/// soll nicht unbemerkt ein Programm starten. <c>file:</c>, <c>javascript:</c> und fremde Schemata
/// wie <c>ms-settings:</c> werden verworfen; über sie startete ein Klick lokale Programme.
/// </para>
/// </remarks>
internal static class PreviewNavigationPolicy
{
    /// <summary>URL-Schema für interne WikiLink-Navigation.</summary>
    public const string WikiLinkScheme = "mdexplorer:";

    private static readonly string[] ExternalSchemes = [Uri.UriSchemeHttp, Uri.UriSchemeHttps, Uri.UriSchemeMailto];

    /// <summary>Ordnet eine Navigation einer der vier Behandlungen zu.</summary>
    /// <param name="uri">Ziel der Navigation, wie WebView2 es meldet.</param>
    /// <param name="isUserInitiated">Ob ein Klick des Nutzers die Navigation ausgelöst hat.</param>
    /// <returns>Die Behandlung.</returns>
    public static PreviewNavigationKind Classify(string uri, bool isUserInitiated)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!isUserInitiated)
        {
            return IsStringDocument(uri) ? PreviewNavigationKind.Document : PreviewNavigationKind.Blocked;
        }

        return ClassifyLink(uri);
    }

    /// <summary>
    /// Dieselbe Zuordnung für eine Fläche, deren eigenes Dokument anderswo liegt, etwa als Datei.
    /// Das eigene Dokument bleibt in der Fläche, auch mit Sprungmarke und auch nach einem Klick.
    /// </summary>
    /// <param name="uri">Ziel der Navigation, wie WebView2 es meldet.</param>
    /// <param name="isUserInitiated">Ob ein Klick des Nutzers die Navigation ausgelöst hat.</param>
    /// <param name="isOwnDocument">Erkennt das Dokument, das die Anwendung selbst lädt.</param>
    /// <returns>Die Behandlung.</returns>
    public static PreviewNavigationKind Classify(string uri, bool isUserInitiated, Func<string, bool> isOwnDocument)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(isOwnDocument);

        if (isOwnDocument(uri))
        {
            return PreviewNavigationKind.Document;
        }

        return isUserInitiated ? ClassifyLink(uri) : PreviewNavigationKind.Blocked;
    }

    /// <summary>Ob die Fläche ein Dokument zeigt, das per <c>NavigateToString</c> kam.</summary>
    /// <param name="uri">Adresse, wie WebView2 sie meldet.</param>
    /// <remarks>Je nach Laufzeitfassung heißt es <c>about:blank</c> oder trägt eine <c>data:</c>-Adresse.</remarks>
    public static bool IsStringDocument(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase)
            || uri.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Liest das Ziel aus einem WikiLink-Verweis.</summary>
    /// <param name="uri">Ein Verweis mit <see cref="WikiLinkScheme"/>.</param>
    /// <returns>Das dekodierte Ziel, oder <see langword="null"/>, wenn keines angegeben ist.</returns>
    public static string? WikiLinkTarget(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        string target = Uri.UnescapeDataString(uri[WikiLinkScheme.Length..].TrimStart('/'));

        return string.IsNullOrWhiteSpace(target) ? null : target;
    }

    private static PreviewNavigationKind ClassifyLink(string uri)
    {
        if (uri.StartsWith(WikiLinkScheme, StringComparison.OrdinalIgnoreCase))
        {
            return PreviewNavigationKind.WikiLink;
        }

        return Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed)
            && ExternalSchemes.Contains(parsed.Scheme, StringComparer.OrdinalIgnoreCase)
                ? PreviewNavigationKind.External
                : PreviewNavigationKind.Blocked;
    }
}
