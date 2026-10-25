using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.Core.Models;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Hält fest, dass ein relatives Bild in der Vorschau ankommt.
/// </summary>
/// <remarks>
/// <para>
/// Die Vorschau wird per <c>NavigateToString</c> geladen; die Basis-Adresse ist damit
/// <c>about:blank</c>. Ein relativer Pfad wie <c>docs/screenshots/suche.png</c> hat dort
/// nichts, worauf er sich beziehen könnte. Ohne Umschreiben bliebe jedes Bild einer Notiz
/// unsichtbar, auch die Aufnahmen im eigenen README.
/// </para>
/// <para>
/// Was nicht angefasst wird, steht hier genauso fest: Ein Bild aus dem Netz behält seine
/// Adresse. Ob es lädt, entscheidet die Sicherheitsregel, und die lässt es ab Werk nicht zu.
/// Abzeichen aus dem Netz bleiben in der Vorschau deshalb leer, solange der Nutzer
/// Netzbilder nicht freigibt.
/// </para>
/// </remarks>
public sealed class PreviewImageSourceTests
{
    private static readonly PreviewImageBase Base = PreviewImageBase.ForFile(@"C:\notes\projekt\doku.md", "projekt/doku.md");

    private readonly PreviewHtmlBuilder _sut = new(new FakeThemeProvider(isDarkMode: false), new FakeSettingsService(AppSettings.Default));

    [Theory]
    [InlineData("docs/screenshots/suche.png", "projekt/docs/screenshots/suche.png")]
    [InlineData("bilder/plan.png", "projekt/bilder/plan.png")]
    [InlineData("./neben.png", "projekt/neben.png")]
    [InlineData("../oben.png", "oben.png")]
    public void Build_OnRelativeImage_PointsAtTheFileBesideTheDocument(string source, string expected)
    {
        string html = _sut.Build($"<p><img src=\"{source}\" alt=\"x\" /></p>", Base);

        Assert.Contains($"src=\"https://{PreviewHtmlBuilder.ImageHost}/{expected}\"", html, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Ohne bekannten Ort des Dokuments gibt es nichts, worauf sich ein relativer Pfad beziehen
    /// könnte. Er bleibt stehen und lädt nicht, statt auf eine zufällige Datei zu zeigen.
    /// </remarks>
    [Fact]
    public void Build_WithoutAnImageBase_LeavesRelativeImagesAlone()
    {
        string html = _sut.Build("<p><img src=\"bild.png\" alt=\"x\" /></p>", imageBase: null);

        Assert.Contains("src=\"bild.png\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OnAnImageAboveTheRoot_LeavesItAlone()
    {
        string html = _sut.Build("<p><img src=\"../../fremd.png\" alt=\"x\" /></p>", Base);

        Assert.Contains("src=\"../../fremd.png\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://img.shields.io/badge/License-MIT-yellow.svg")]
    [InlineData("http://example.org/bild.png")]
    [InlineData("data:image/png;base64,iVBORw0KGgo=")]
    public void Build_OnAbsoluteImage_LeavesItAlone(string source)
    {
        string html = _sut.Build($"<p><img src=\"{source}\" alt=\"x\" /></p>", Base);

        Assert.Contains($"src=\"{source}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain(PreviewHtmlBuilder.ImageHost + "/" + source, html, StringComparison.Ordinal);
    }

    [Fact]
    public void ContentSecurityPolicy_ForImages_AllowsTheImageHostAndNothingElse()
    {
        Assert.Contains("img-src 'self' data: https://" + PreviewHtmlBuilder.ImageHost, PreviewHtmlBuilder.ContentSecurityPolicy, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", PreviewHtmlBuilder.ContentSecurityPolicy, StringComparison.Ordinal);
        Assert.Contains("script-src 'none'", PreviewHtmlBuilder.ContentSecurityPolicy, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageHost_OnTheNetwork_CannotBeResolved()
    {
        // RFC 2606 reserviert .invalid ausdrücklich dafür, niemals aufgelöst zu werden.
        // Ein Tippfehler im Namen kann damit keine Anfrage nach draußen auslösen.
        Assert.EndsWith(".invalid", PreviewHtmlBuilder.ImageHost, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OnSeveralImages_RewritesEveryRelativeOne()
    {
        string html = _sut.Build(
            "<img src=\"eins.png\"><img src=\"https://example.org/zwei.png\"><img src=\"drei/vier.png\">", Base);

        Assert.Equal(2, CountOccurrences(html, "https://" + PreviewHtmlBuilder.ImageHost + "/"));
        Assert.Contains("src=\"https://example.org/zwei.png\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OnTextWithoutImages_ChangesNothing()
    {
        const string body = "<p>Ein Satz ohne Bild, aber mit src= im Text.</p>";

        Assert.Contains(body, _sut.Build(body, Base), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OnDefaultSettings_BlocksRemoteImages()
    {
        // Ab Werk aus: Die Anwendung arbeitet vollständig ohne Internetverbindung, und ein
        // Bild aus dem Netz verriete einem fremden Server, wann welche Notiz offen war.
        string html = _sut.Build("<img src=\"https://img.shields.io/badge/x.svg\">");

        Assert.Contains("img-src 'self' data: https://" + PreviewHtmlBuilder.ImageHost, html, StringComparison.Ordinal);
        Assert.DoesNotContain("img-src 'self' data: https: ", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WhenUserAllowsRemoteImages_OpensOnlyTheImageSource()
    {
        AppSettings allowed = AppSettings.Default with
        {
            Behavior = AppSettings.Default.Behavior with { LoadRemoteImagesInPreview = true },
        };
        PreviewHtmlBuilder sut = new(new FakeThemeProvider(isDarkMode: false), new FakeSettingsService(allowed));

        string html = sut.Build("<img src=\"https://img.shields.io/badge/x.svg\">");

        Assert.Contains("img-src 'self' data: https:", html, StringComparison.Ordinal);
        // Geöffnet wird ausschließlich die Bildquelle, alles Übrige bleibt gesperrt.
        Assert.Contains("script-src 'none'", html, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", html, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }
        return count;
    }
}
