using System.Text.RegularExpressions;
using MdExplorer.App.Services;
using MdExplorer.App.Services.Help;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.Core.Models;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Vorschau, Hilfe und Graph zeigen Roboto Flex und Roboto Mono aus der Programmdatei, und ihre
/// Sicherheitsregeln lassen Schriften nur aus dieser Quelle zu.
/// </summary>
public sealed partial class WebFontFacesTests
{
    // Die Kennung am Anfang jeder WOFF2-Datei.
    private const string Woff2Signature = "wOF2";

    [Fact]
    public void Css_DeclaresBothFamiliesForLatinAndLatinExtended()
    {
        string css = WebFontFaces.Css;

        Assert.Equal(4, FontFaceRule().Count(css));
        Assert.Equal(2, Regex.Count(css, "font-family: \"Roboto Flex\"", RegexOptions.CultureInvariant));
        Assert.Equal(2, Regex.Count(css, "font-family: \"Roboto Mono\"", RegexOptions.CultureInvariant));
    }

    [Fact]
    public void Css_CarriesTheFontFilesThemselvesAndNoAddress()
    {
        MatchCollection sources = DataSource().Matches(WebFontFaces.Css);

        Assert.Equal(4, sources.Count);
        Assert.All(sources, source =>
        {
            byte[] file = Convert.FromBase64String(source.Groups["data"].Value);
            Assert.Equal(Woff2Signature, System.Text.Encoding.ASCII.GetString(file, 0, Woff2Signature.Length));
        });
        // Jede Quelle ist die Datei selbst; eine Adresse, die der Browser nachladen müsste, gibt es nicht.
        Assert.Equal(sources.Count, Regex.Count(WebFontFaces.Css, "url\\(", RegexOptions.CultureInvariant));
    }

    [Theory]
    [InlineData(PreviewHtmlBuilder.ContentSecurityPolicy)]
    [InlineData(PreviewHtmlBuilder.ContentSecurityPolicyWithRemoteImages)]
    public void PreviewPolicies_AllowFontsOnlyFromTheDocumentItself(string policy) => AssertFontsOnlyFromData(policy);

    [Fact]
    public void Preview_PutsTheFontRulesBeforeItsStylesheet()
    {
        PreviewHtmlBuilder sut = new(new FakeThemeProvider(isDarkMode: false), new FakeSettingsService(AppSettings.Default));

        string html = sut.Build("<p>x</p>");

        int fontFace = html.IndexOf("@font-face", StringComparison.Ordinal);
        int stylesheetFamily = html.IndexOf("font-family: 'Roboto Flex'", StringComparison.Ordinal);
        Assert.InRange(fontFace, 0, int.MaxValue);
        Assert.True(fontFace < stylesheetFamily, "Die Schriftregeln müssen vor dem Stylesheet stehen, das sie benutzt.");
    }

    [Fact]
    public void Help_CarriesTheFontsAndAllowsThemOnlyFromTheDocument()
    {
        string html = HelpDocumentBuilder.Build("<p>Hilfe</p>", isDarkMode: false);

        Assert.Contains(WebFontFaces.Css, html, StringComparison.Ordinal);
        AssertFontsOnlyFromData(Policy(html));
    }

    [Fact]
    public void Graph_CarriesTheFontsAndLabelsItsNodesWithThem()
    {
        string html = GraphDocumentBuilder.Build("{\"nodes\":[],\"edges\":[]}", isDarkMode: false);

        Assert.Contains(WebFontFaces.Css, html, StringComparison.Ordinal);
        AssertFontsOnlyFromData(Policy(html));
        Assert.Contains("const LABEL_FONT_FAMILY = \"'Roboto Flex'", html, StringComparison.Ordinal);
        Assert.Contains("document.fonts.load(", html, StringComparison.Ordinal);
    }

    private static void AssertFontsOnlyFromData(string policy)
    {
        Match fontSource = Assert.Single(FontSourceDirective().Matches(policy));
        Assert.Equal("data:", fontSource.Groups["sources"].Value.Trim());
    }

    private static string Policy(string html) => PolicyMeta().Match(html).Groups["policy"].Value;

    [GeneratedRegex("@font-face", RegexOptions.CultureInvariant)]
    private static partial Regex FontFaceRule();

    [GeneratedRegex("url\\(\"data:font/woff2;base64,(?<data>[A-Za-z0-9+/=]+)\"\\)", RegexOptions.CultureInvariant)]
    private static partial Regex DataSource();

    [GeneratedRegex("font-src(?<sources>[^;]*)", RegexOptions.CultureInvariant)]
    private static partial Regex FontSourceDirective();

    [GeneratedRegex("http-equiv=\"Content-Security-Policy\" content=\"(?<policy>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex PolicyMeta();
}
