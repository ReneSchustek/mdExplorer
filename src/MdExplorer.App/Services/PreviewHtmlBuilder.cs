using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;

namespace MdExplorer.App.Services;

/// <summary>
/// Verpackt das vom Parser gelieferte HTML in ein vollständiges Dokument: Doctype,
/// strikte Content-Security-Policy, eingebettetes Stylesheet und die eingestellte Schriftgröße.
/// Das Erscheinungsbild geht als Kennzeichen am Wurzelelement mit; beide Belegungen stehen im
/// selben Stylesheet.
/// </summary>
internal sealed partial class PreviewHtmlBuilder
{
    /// <summary>
    /// Sicherheitsregel der Vorschau im Normalfall. Kein Skript läuft (<c>script-src 'none'</c>),
    /// alles nicht ausdrücklich Genannte ist gesperrt (<c>default-src 'none'</c>). Zugelassen sind
    /// das eingebettete Stylesheet und Bilder als <c>data:</c>-URI oder aus der Index-Wurzel des
    /// Dokuments über <see cref="ImageHost"/>.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'none'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https://"
        + ImageHost + "; script-src 'none'";

    /// <summary>
    /// Die Regel, wenn der Nutzer Bilder aus dem Netz ausdrücklich zugelassen hat.
    /// </summary>
    /// <remarks>
    /// Geöffnet wird ausschließlich <c>img-src</c>, und nur für <c>https</c>. Skripte,
    /// Stilvorlagen und alles Übrige bleiben gesperrt. Ein Bild darf geladen werden, mehr
    /// nicht.
    /// </remarks>
    public const string ContentSecurityPolicyWithRemoteImages =
        "default-src 'none'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https: https://"
        + ImageHost + "; script-src 'none'";

    /// <summary>
    /// Virtueller Name für den Ordner, aus dem die Bilder des angezeigten Dokuments kommen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Die Vorschau wird per <c>NavigateToString</c> geladen; die Basis-Adresse des Dokuments
    /// ist damit <c>about:blank</c>. Ein relativer Bildpfad wie
    /// <c>docs/screenshots/suche.png</c> hat dort nichts, worauf er sich beziehen könnte, und
    /// kein Bild einer Notiz wäre zu sehen. Ein <c>file:///</c>-Pfad hilft nicht: Chromium lädt
    /// keine Datei-Unterressourcen in ein Dokument fremder Herkunft.
    /// </para>
    /// <para>
    /// Der Weg, den WebView2 dafür vorsieht, ist ein virtueller Rechnername, der auf einen
    /// Ordner zeigt. Die Endung <c>.invalid</c> ist nach RFC 2606 dafür reserviert, niemals
    /// im Netz aufgelöst zu werden; der Name kann also unter keinen Umständen irgendwo
    /// landen. Zugelassen wird er in der Sicherheitsregel ausdrücklich und einzeln; alles
    /// andere bleibt gesperrt, insbesondere Abzeichen und Bilder aus dem Netz.
    /// </para>
    /// </remarks>
    public const string ImageHost = "dokument.invalid";

    private const string StylesheetResource = "MdExplorer.App.Assets.preview.css";

    private readonly Lazy<string> _css = new(() => EmbeddedDocument.ReadAsset(StylesheetResource));
    private readonly IEffectiveThemeProvider _themeProvider;
    private readonly ISettingsService _settings;

    /// <summary>Erzeugt den Baustein; das Stylesheet wird erst beim ersten Bedarf gelesen.</summary>
    public PreviewHtmlBuilder(IEffectiveThemeProvider themeProvider, ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(themeProvider);
        ArgumentNullException.ThrowIfNull(settings);
        _themeProvider = themeProvider;
        _settings = settings;
    }

    /// <summary>
    /// Ob sich zwischen zwei Ständen etwas geändert hat, das in den Aufbau der Vorschau eingeht.
    /// </summary>
    /// <remarks>
    /// Die Vorschau ist ein fertiges Dokument und hört auf keine Einstellung. Wer sie besitzt, baut
    /// sie nach einer solchen Änderung neu auf; nach jeder anderen nicht.
    /// </remarks>
    /// <param name="previous">Der Stand vor dem Speichern.</param>
    /// <param name="current">Der gespeicherte Stand.</param>
    /// <returns><see langword="true"/> bei geändertem Erscheinungsbild, geänderter Schriftgröße oder geänderter Freigabe für Netzbilder.</returns>
    public static bool DependsOn(AppSettings previous, AppSettings current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        return previous.Appearance.Theme != current.Appearance.Theme
            || previous.Appearance.PreviewFontSize != current.Appearance.PreviewFontSize
            || previous.Behavior.LoadRemoteImagesInPreview != current.Behavior.LoadRemoteImagesInPreview;
    }

    /// <summary>Baut das vollständige HTML-Dokument eines Inhalts ohne bekannten Ort; relative Bilder bleiben unaufgelöst.</summary>
    public string Build(string bodyHtml) => Build(bodyHtml, imageBase: null);

    /// <summary>Baut das vollständige HTML-Dokument inklusive CSP, Theme-CSS und Schriftgröße.</summary>
    /// <param name="bodyHtml">Das gerenderte Dokument.</param>
    /// <param name="imageBase">Wogegen relative Bildpfade aufgelöst werden; ohne sie bleiben sie stehen.</param>
    /// <returns>Das vollständige HTML-Dokument.</returns>
    /// <exception cref="RegexMatchTimeoutException">Das Umschreiben der Bildpfade brauchte zu lange; ohne Basis kann das nicht eintreten.</exception>
    public string Build(string bodyHtml, PreviewImageBase? imageBase)
    {
        ArgumentNullException.ThrowIfNull(bodyHtml);

        AppSettings settings = _settings.Current;
        string css = _css.Value;
        string theme = _themeProvider.IsDarkMode ? "dark" : "light";
        string policy = settings.Behavior.LoadRemoteImagesInPreview
            ? ContentSecurityPolicyWithRemoteImages
            : ContentSecurityPolicy;
        string body = imageBase is null ? bodyHtml : RewriteRelativeImageSources(bodyHtml, imageBase);
        // 512 Zeichen decken den Rahmen samt Sicherheitsregel und Schriftgröße; er braucht knapp 330.
        StringBuilder builder = new(body.Length + css.Length + 512);
        _ = builder.Append("<!doctype html>")
            .Append(CultureInfo.InvariantCulture, $"<html lang=\"de\" data-theme=\"{theme}\"><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"{policy}\"><style>")
            .Append(css)
            // Hinter dem Stylesheet, damit die Einstellung gewinnt. Der Wert ist nach der Prüfung
            // der Einstellungen eine ganze Zahl in festen Grenzen und kann nichts einschleusen.
            .Append(CultureInfo.InvariantCulture, $"body {{ font-size: {settings.Appearance.PreviewFontSize}px; }}")
            .Append("</style></head><body>")
            .Append(body)
            .Append("</body></html>");
        return builder.ToString();
    }

    /// <summary>Liefert ein leeres Preview-Dokument (Theme-Hintergrund, kein Inhalt).</summary>
    public string BuildEmpty() => Build(string.Empty, imageBase: null);

    /// <summary>
    /// Lässt relative Bildpfade auf die Datei zeigen, die sie vom Dokument aus meinen.
    /// </summary>
    /// <remarks>
    /// Angefasst wird nur, was relativ ist. Ein <c>data:</c>-Bild, ein Sprungziel, ein Bild aus
    /// dem Netz und ein Pfad mit Laufwerk bleiben stehen; ebenso ein Pfad über die Wurzel hinaus.
    /// Das Bild aus dem Netz weist die Sicherheitsregel ab, solange der Nutzer Netzbilder nicht
    /// zugelassen hat.
    /// </remarks>
    private static string RewriteRelativeImageSources(string bodyHtml, PreviewImageBase imageBase)
    {
        if (bodyHtml.Length == 0 || !bodyHtml.Contains("<img", StringComparison.OrdinalIgnoreCase))
        {
            return bodyHtml;
        }

        return ImageSourcePattern().Replace(bodyHtml, match =>
        {
            string source = match.Groups["src"].Value;
            string? resolved = IsAbsolute(source) ? null : imageBase.Resolve(source);
            return resolved is null ? match.Value : match.Groups["prefix"].Value + resolved;
        });
    }

    private static bool IsAbsolute(string source) =>
        source.Length == 0
        || source.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        || source.StartsWith('#')
        || Uri.TryCreate(source, UriKind.Absolute, out _);

    [GeneratedRegex(
        "(?<prefix><img\\b[^>]*?\\bsrc\\s*=\\s*\")(?<src>[^\"]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        // Obergrenze gegen ausuferndes Rückverfolgen bei kaputtem HTML; reißt sie, wirft
        // Replace eine RegexMatchTimeoutException.
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ImageSourcePattern();
}
