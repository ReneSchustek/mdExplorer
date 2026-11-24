using System.IO;
using System.Text;

namespace MdExplorer.App.Services;

/// <summary>
/// Die Schriften der Gestaltungslinie für die HTML-Seiten in WebView2 (Vorschau, Hilfe, Graph):
/// Roboto Flex und Roboto Mono als <c>@font-face</c>-Regeln, die Dateien als <c>data:</c>-URI.
/// </summary>
/// <remarks>
/// <para>
/// Eingebettet statt verlinkt: Vorschau und Hilfe kommen per <c>NavigateToString</c> und haben
/// keine Herkunft, von der sie nachladen könnten; der Graph kommt aus einer Datei, und eine
/// Datei-Seite lädt keine Schrift von einem anderen Ort ohne CORS-Freigabe. Ein <c>data:</c>-URI
/// braucht weder Host noch Freigabe und verrät niemandem im Netz, dass das Programm läuft.
/// </para>
/// <para>
/// Die vier Dateien sind zusammen gut 110 KB, als Base64 rund 150 KB je Seite. Das bleibt weit
/// unter der Grenze von 2 MB, an der <c>NavigateToString</c> abbricht.
/// </para>
/// </remarks>
internal static class WebFontFaces
{
    /// <summary>
    /// Der Teil der Sicherheitsregel, der genau diese Schriften zulässt und keine aus dem Netz.
    /// </summary>
    public const string ContentSecurityPolicySource = "font-src data:";

    private const string ResourcePrefix = "MdExplorer.App.Assets.Fonts.";

    // Die Bereiche, mit denen Google Fonts die Dateien zuschneidet. Der Browser holt eine Datei
    // nur, wenn die Seite ein Zeichen aus ihrem Bereich zeigt.
    private const string LatinRange =
        "U+0000-00FF, U+0131, U+0152-0153, U+02BB-02BC, U+02C6, U+02DA, U+02DC, U+0304, U+0308, "
        + "U+0329, U+2000-206F, U+20AC, U+2122, U+2191, U+2193, U+2212, U+2215, U+FEFF, U+FFFD";

    private const string LatinExtendedRange =
        "U+0100-02BA, U+02BD-02C5, U+02C7-02CC, U+02CE-02D7, U+02DD-02FF, U+1E00-1E9F, "
        + "U+20A0-20AB, U+20AD-20C0, U+2C60-2C7F, U+A720-A7FF";

    // Die Gewichtsachsen der Dateien, wie Google Fonts sie ausliefert.
    private const string FlexWeights = "300 900";
    private const string MonoWeights = "400 700";

    private static readonly Lazy<string> Rules = new(BuildRules);

    /// <summary>Die vier <c>@font-face</c>-Regeln; beim ersten Zugriff einmal gebaut.</summary>
    /// <exception cref="InvalidOperationException">Eine Schriftdatei fehlt in der Bibliothek.</exception>
    public static string Css => Rules.Value;

    private static string BuildRules()
    {
        StringBuilder css = new();
        AppendFace(css, "Roboto Flex", FlexWeights, "roboto-flex-latin.woff2", LatinRange);
        AppendFace(css, "Roboto Flex", FlexWeights, "roboto-flex-latin-ext.woff2", LatinExtendedRange);
        AppendFace(css, "Roboto Mono", MonoWeights, "roboto-mono-latin.woff2", LatinRange);
        AppendFace(css, "Roboto Mono", MonoWeights, "roboto-mono-latin-ext.woff2", LatinExtendedRange);
        return css.ToString();
    }

    private static void AppendFace(StringBuilder css, string family, string weights, string fileName, string unicodeRange)
    {
        _ = css.Append("@font-face { font-family: \"").Append(family)
            .Append("\"; font-style: normal; font-weight: ").Append(weights)
            .Append("; font-display: swap; src: url(\"data:font/woff2;base64,").Append(ReadBase64(fileName))
            .Append("\") format(\"woff2\"); unicode-range: ").Append(unicodeRange).Append("; }\n");
    }

    private static string ReadBase64(string fileName)
    {
        string resourceName = ResourcePrefix + fileName;
        using Stream? stream = typeof(WebFontFaces).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Eingebettete Ressource fehlt: {resourceName}.");
        }
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
