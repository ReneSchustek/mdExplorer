using MdExplorer.Parser.Services;
using MdExplorer.Parser.Tests.Helpers;

namespace MdExplorer.Parser.Tests.Services;

/// <summary>
/// Das HTML des Parsers trägt keine ausführbaren Verweise und keine Ereignis- oder Stilattribute,
/// auch wenn die Notiz sie enthält.
/// </summary>
/// <remarks>
/// Die Notizen stammen nicht alle vom Nutzer selbst. Bisher hielt allein die Sicherheitsregel der
/// Vorschau (<c>script-src 'none'</c>) ein <c>javascript:</c>-Ziel oder ein <c>onclick</c> davon
/// ab, etwas auszuführen; über ein <c>style</c>-Attribut ließ sich die Vorschau trotzdem
/// überdecken. Das HTML selbst soll sauber sein, damit es nicht von einer einzigen Schutzschicht
/// abhängt.
/// </remarks>
public sealed class MarkdigParserSafetyTests
{
    private readonly MarkdigParser _sut = new(
        new FrontmatterExtractor(),
        new TagExtractor(new FakeSettingsService()),
        new WikiLinkExtractor(),
        new TagNormalizer());

    [Theory]
    [InlineData("[klick](javascript:alert(1))", "javascript:")]
    [InlineData("[klick](JavaScript:alert(1))", "avascript:")]
    [InlineData("[datei](file:///C:/Windows/win.ini)", "file:")]
    [InlineData("[einstellungen](ms-settings:privacy)", "ms-settings:")]
    [InlineData("![bild](file:///C:/geheim.png)", "file:")]
    [InlineData("[daten](data:text/html,<b>x</b>)", "data:text/html")]
    public void Parse_WithAnUnsafeLinkTarget_DropsTheTarget(string markdown, string forbidden)
    {
        string html = Render(markdown);

        Assert.DoesNotContain(forbidden, html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("[seite](https://example.org/a)", "href=\"https://example.org/a\"")]
    [InlineData("[alt](http://example.org)", "href=\"http://example.org\"")]
    [InlineData("[mail](mailto:jemand@example.org)", "href=\"mailto:jemand@example.org\"")]
    [InlineData("[notiz](ordner/notiz.md#abschnitt)", "href=\"ordner/notiz.md#abschnitt\"")]
    [InlineData("[anker](#oben)", "href=\"#oben\"")]
    [InlineData("[[Projekt X]]", "href=\"mdexplorer://projekt-x\"")]
    [InlineData("![bild](bilder/plan.png)", "src=\"bilder/plan.png\"")]
    [InlineData("![punkt](data:image/png;base64,iVBORw0KGgo=)", "src=\"data:image/png;base64,iVBORw0KGgo=\"")]
    public void Parse_WithASafeLinkTarget_KeepsIt(string markdown, string expected)
    {
        Assert.Contains(expected, Render(markdown), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WithEventAndStyleAttributes_DropsThem()
    {
        string html = Render("# Titel {onclick=\"alert(1)\" style=\"position:fixed\" onMouseOver=\"x()\"}");

        Assert.DoesNotContain("onclick", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onmouseover", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_WithAnAnchorAttribute_KeepsIt()
    {
        // Das Handbuch springt über solche Anker in seine Abschnitte.
        Assert.Contains("id=\"inhalt\"", Render("## Inhalt {#inhalt}"), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WithAVideoLink_RendersNoFrame()
    {
        string html = Render("![video](https://www.youtube.com/watch?v=abc)");

        Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_WithRawHtml_EscapesIt()
    {
        string html = Render("<script>alert(1)</script>");

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    private string Render(string markdown) => GzipHelper.Decompress(_sut.Parse(markdown).RenderedHtmlGz);
}
