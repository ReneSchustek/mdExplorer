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
/// nichts, worauf er sich beziehen könnte — <b>kein Bild einer Notiz war je zu sehen</b>,
/// und aufgefallen ist es erst am 16.08.2026 an den vier Aufnahmen im eigenen README.
/// </para>
/// <para>
/// Was <b>nicht</b> angefasst wird, steht hier genauso fest: Ein Bild aus dem Netz bleibt
/// stehen und wird von der Sicherheitsregel abgewiesen. Die drei Abzeichen oben im README
/// sind deshalb weiterhin leer — das ist die Entscheidung „keine Netzwerkquellen in der
/// Vorschau", nicht ein zweiter Fehler.
/// </para>
/// </remarks>
public sealed class PreviewImageSourceTests
{
    private readonly PreviewHtmlBuilder _sut = new(new FakeThemeProvider(isDarkMode: false), new FakeSettingsService(AppSettings.Default));

    [Theory]
    [InlineData("docs/screenshots/suche.png")]
    [InlineData("bilder/plan.png")]
    [InlineData("./neben.png")]
    [InlineData("../oben.png")]
    public void Build_OnRelativeImage_PointsAtTheDocumentFolder(string source)
    {
        string html = _sut.Build($"<p><img src=\"{source}\" alt=\"x\" /></p>");

        Assert.Contains($"src=\"https://{PreviewHtmlBuilder.ImageHost}/", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"src=\"{source}\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://img.shields.io/badge/License-MIT-yellow.svg")]
    [InlineData("http://example.org/bild.png")]
    [InlineData("data:image/png;base64,iVBORw0KGgo=")]
    public void Build_OnAbsoluteImage_LeavesItAlone(string source)
    {
        string html = _sut.Build($"<p><img src=\"{source}\" alt=\"x\" /></p>");

        Assert.Contains($"src=\"{source}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain(PreviewHtmlBuilder.ImageHost + "/" + source, html, StringComparison.Ordinal);
    }

    [Fact]
    public void ContentSecurityPolicy_AllowsTheDocumentFolderAndNothingElse()
    {
        Assert.Contains("img-src 'self' data: https://" + PreviewHtmlBuilder.ImageHost, PreviewHtmlBuilder.ContentSecurityPolicy, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", PreviewHtmlBuilder.ContentSecurityPolicy, StringComparison.Ordinal);
        Assert.Contains("script-src 'none'", PreviewHtmlBuilder.ContentSecurityPolicy, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageHost_CannotBeResolvedOnTheNetwork()
    {
        // RFC 2606 reserviert .invalid ausdrücklich dafür, niemals aufgelöst zu werden.
        // Ein Tippfehler im Namen kann damit keine Anfrage nach draußen auslösen.
        Assert.EndsWith(".invalid", PreviewHtmlBuilder.ImageHost, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OnSeveralImages_RewritesEveryRelativeOne()
    {
        string html = _sut.Build(
            "<img src=\"eins.png\"><img src=\"https://example.org/zwei.png\"><img src=\"drei/vier.png\">");

        Assert.Equal(2, CountOccurrences(html, "https://" + PreviewHtmlBuilder.ImageHost + "/"));
        Assert.Contains("src=\"https://example.org/zwei.png\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OnTextWithoutImages_ChangesNothing()
    {
        const string body = "<p>Ein Satz ohne Bild, aber mit src= im Text.</p>";

        Assert.Contains(body, _sut.Build(body), StringComparison.Ordinal);
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
        AppSettings erlaubt = AppSettings.Default with
        {
            Behavior = AppSettings.Default.Behavior with { LoadRemoteImagesInPreview = true },
        };
        PreviewHtmlBuilder sut = new(new FakeThemeProvider(isDarkMode: false), new FakeSettingsService(erlaubt));

        string html = sut.Build("<img src=\"https://img.shields.io/badge/x.svg\">");

        Assert.Contains("img-src 'self' data: https:", html, StringComparison.Ordinal);
        // Geöffnet wird ausschließlich die Bildquelle — alles Übrige bleibt gesperrt.
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
