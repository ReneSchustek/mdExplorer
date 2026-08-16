using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MdExplorer.Core.Abstractions;

namespace MdExplorer.App.Services;

/// <summary>
/// Verpackt das vom Parser gelieferte HTML in ein vollständiges Dokument: Doctype,
/// strikte Content-Security-Policy, eingebettetes Theme-CSS. Lädt die CSS-Assets
/// einmalig per Reflection aus den Embedded-Resources des App-Assemblies.
/// </summary>
internal sealed partial class PreviewHtmlBuilder
{
    /// <summary>
    /// Pflicht-CSP für den Preview. Es laufen keinerlei Skripte (`script-src 'none'`),
    /// es werden keine externen Quellen geladen (`default-src 'none'`); zugelassen sind nur das
    /// eingebettete Theme-CSS plus Inline-Style und Bilder als <c>data:</c>-URI.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'none'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https://"
        + ImageHost + "; script-src 'none'";

    /// <summary>
    /// Die Regel, wenn der Nutzer Bilder aus dem Netz ausdrücklich zugelassen hat.
    /// </summary>
    /// <remarks>
    /// Geöffnet wird ausschließlich <c>img-src</c>, und nur für <c>https</c>. Skripte,
    /// Stilvorlagen und alles Übrige bleiben gesperrt — ein Bild darf geladen werden, mehr
    /// nicht.
    /// </remarks>
    public const string ContentSecurityPolicyWithRemoteImages =
        "default-src 'none'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https: https://"
        + ImageHost + "; script-src 'none'";

    /// <summary>
    /// Virtueller Name für den Ordner des angezeigten Dokuments.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Die Vorschau wird per <c>NavigateToString</c> geladen; die Basis-Adresse des Dokuments
    /// ist damit <c>about:blank</c>. Ein relativer Bildpfad wie
    /// <c>docs/screenshots/suche.png</c> hat dort nichts, worauf er sich beziehen könnte —
    /// <b>kein einziges Bild einer Notiz war je zu sehen</b>. Ein <c>file:///</c>-Pfad hilft
    /// nicht: Chromium lädt keine Datei-Unterressourcen in ein Dokument fremder Herkunft.
    /// </para>
    /// <para>
    /// Der Weg, den WebView2 dafür vorsieht, ist ein virtueller Rechnername, der auf einen
    /// Ordner zeigt. Die Endung <c>.invalid</c> ist nach RFC 2606 dafür reserviert, niemals
    /// im Netz aufgelöst zu werden — der Name kann also unter keinen Umständen irgendwo
    /// landen. Zugelassen wird er in der Sicherheitsregel ausdrücklich und einzeln; alles
    /// andere bleibt gesperrt, insbesondere Abzeichen und Bilder aus dem Netz.
    /// </para>
    /// </remarks>
    public const string ImageHost = "dokument.invalid";

    private const string LightResource = "MdExplorer.App.Assets.preview-light.css";
    private const string DarkResource = "MdExplorer.App.Assets.preview-dark.css";

    private readonly Lazy<string> _lightCss;
    private readonly Lazy<string> _darkCss;
    private readonly IEffectiveThemeProvider _themeProvider;
    private readonly ISettingsService _settings;

    /// <summary>Konstruktor mit injizierbaren Abhängigkeiten — für Tests.</summary>
    public PreviewHtmlBuilder(IEffectiveThemeProvider themeProvider, ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(themeProvider);
        ArgumentNullException.ThrowIfNull(settings);
        _themeProvider = themeProvider;
        _settings = settings;
        Assembly assembly = typeof(PreviewHtmlBuilder).Assembly;
        _lightCss = new Lazy<string>(() => LoadResource(assembly, LightResource));
        _darkCss = new Lazy<string>(() => LoadResource(assembly, DarkResource));
    }

    /// <summary>Baut das vollständige HTML-Dokument inklusive CSP und Theme-CSS.</summary>
    public string Build(string bodyHtml)
    {
        ArgumentNullException.ThrowIfNull(bodyHtml);

        string css = _themeProvider.IsDarkMode ? _darkCss.Value : _lightCss.Value;
        string policy = _settings.Current.Behavior.LoadRemoteImagesInPreview
            ? ContentSecurityPolicyWithRemoteImages
            : ContentSecurityPolicy;
        string body = RewriteRelativeImageSources(bodyHtml);
        StringBuilder builder = new(body.Length + css.Length + 512);
        _ = builder.Append("<!doctype html>")
            .Append(CultureInfo.InvariantCulture, $"<html lang=\"de\"><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"{policy}\"><style>")
            .Append(css)
            .Append("</style></head><body>")
            .Append(body)
            .Append("</body></html>");
        return builder.ToString();
    }

    /// <summary>Liefert ein leeres Preview-Dokument (Theme-Hintergrund, kein Inhalt).</summary>
    public string BuildEmpty() => Build(string.Empty);

    /// <summary>
    /// Hängt relative Bildpfade an den virtuellen Ordner des Dokuments.
    /// </summary>
    /// <remarks>
    /// Angefasst wird nur, was wirklich relativ ist. Ein <c>data:</c>-Bild, ein Bild aus dem
    /// Netz und ein absoluter Pfad bleiben stehen, wie sie sind — das Bild aus dem Netz wird
    /// von der Sicherheitsregel abgewiesen, und das ist so gewollt.
    /// </remarks>
    private static string RewriteRelativeImageSources(string bodyHtml)
    {
        if (bodyHtml.Length == 0 || !bodyHtml.Contains("<img", StringComparison.OrdinalIgnoreCase))
        {
            return bodyHtml;
        }

        return ImageSourcePattern().Replace(bodyHtml, match =>
        {
            string source = match.Groups["src"].Value;
            return IsAbsolute(source)
                ? match.Value
                : match.Groups["vor"].Value + "https://" + ImageHost + "/" + source.TrimStart('/');
        });
    }

    private static bool IsAbsolute(string source) =>
        source.Length == 0
        || source.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        || source.StartsWith('#')
        || Uri.TryCreate(source, UriKind.Absolute, out _);

    [GeneratedRegex(
        "(?<vor><img\\b[^>]*?\\bsrc\\s*=\\s*\")(?<src>[^\"]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ImageSourcePattern();

    private static string LoadResource(Assembly assembly, string resourceName)
    {
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded-Resource '{resourceName}' nicht gefunden.");
        }
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
