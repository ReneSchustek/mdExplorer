using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace MdExplorer.App.Services;

/// <summary>
/// Verpackt das vom Parser gelieferte HTML in ein vollständiges Dokument: Doctype,
/// strikte Content-Security-Policy, eingebettetes Theme-CSS. Lädt die CSS-Assets
/// einmalig per Reflection aus den Embedded-Resources des App-Assemblies.
/// </summary>
internal sealed class PreviewHtmlBuilder
{
    /// <summary>
    /// Pflicht-CSP für den Preview. Es laufen keinerlei Skripte (`script-src 'none'`),
    /// es werden keine externen Quellen geladen (`default-src 'none'`); zugelassen sind nur das
    /// eingebettete Theme-CSS plus Inline-Style und Bilder als <c>data:</c>-URI.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'none'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; script-src 'none'";

    private const string LightResource = "MdExplorer.App.Assets.preview-light.css";
    private const string DarkResource = "MdExplorer.App.Assets.preview-dark.css";

    private readonly Lazy<string> _lightCss;
    private readonly Lazy<string> _darkCss;
    private readonly ISystemThemeProvider _themeProvider;

    /// <summary>Standard-Konstruktor: verwendet das Windows-Systemtheme.</summary>
    public PreviewHtmlBuilder()
        : this(new SystemThemeProvider())
    {
    }

    /// <summary>Konstruktor mit injizierbarem Theme-Provider — für Tests.</summary>
    public PreviewHtmlBuilder(ISystemThemeProvider themeProvider)
    {
        ArgumentNullException.ThrowIfNull(themeProvider);
        _themeProvider = themeProvider;
        Assembly assembly = typeof(PreviewHtmlBuilder).Assembly;
        _lightCss = new Lazy<string>(() => LoadResource(assembly, LightResource));
        _darkCss = new Lazy<string>(() => LoadResource(assembly, DarkResource));
    }

    /// <summary>Baut das vollständige HTML-Dokument inklusive CSP und Theme-CSS.</summary>
    public string Build(string bodyHtml)
    {
        ArgumentNullException.ThrowIfNull(bodyHtml);

        string css = _themeProvider.IsDarkMode ? _darkCss.Value : _lightCss.Value;
        StringBuilder builder = new(bodyHtml.Length + css.Length + 512);
        _ = builder.Append("<!doctype html>")
            .Append(CultureInfo.InvariantCulture, $"<html lang=\"de\"><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"{ContentSecurityPolicy}\"><style>")
            .Append(css)
            .Append("</style></head><body>")
            .Append(bodyHtml)
            .Append("</body></html>");
        return builder.ToString();
    }

    /// <summary>Liefert ein leeres Preview-Dokument (Theme-Hintergrund, kein Inhalt).</summary>
    public string BuildEmpty() => Build(string.Empty);

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
