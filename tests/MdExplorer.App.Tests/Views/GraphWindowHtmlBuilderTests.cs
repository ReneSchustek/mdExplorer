using System.Text.RegularExpressions;
using MdExplorer.App.Views.Graph;

namespace MdExplorer.App.Tests.Views;

/// <summary>
/// Tests die HTML-Verpackung des Graph-Fensters. Prüfparameter: CSP-Strenge (kein
/// <c>'unsafe-inline'</c>), pro Aufruf neuer Nonce, Snapshot-JSON liegt im nicht-ausführbaren
/// <c>application/json</c>-Datenblock und kann kein <c>&lt;/script&gt;</c> einschleusen.
/// </summary>
public sealed class GraphWindowHtmlBuilderTests
{
    private const string MinimalSnapshot = """{"nodes":[],"edges":[]}""";

    [Fact]
    public void BuildHtml_ProducesScriptSrcWithNonceAndWithoutUnsafeInline()
    {
        string html = GraphWindow.BuildHtml(MinimalSnapshot, isDarkMode: false);

        Match cspMatch = Regex.Match(
            html,
            "<meta http-equiv=\"Content-Security-Policy\" content=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);
        Assert.True(cspMatch.Success);
        string policy = cspMatch.Groups[1].Value;
        string scriptSrc = ExtractDirective(policy, "script-src");
        Assert.Contains("'nonce-", scriptSrc, StringComparison.Ordinal);
        // Skripte werden ausschließlich über die Nonce autorisiert — kein 'unsafe-inline' zulässig.
        Assert.DoesNotContain("'unsafe-inline'", scriptSrc, StringComparison.Ordinal);
    }

    private static string ExtractDirective(string policy, string directive)
    {
        int start = policy.IndexOf(directive, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }
        int end = policy.IndexOf(';', start);
        return end < 0 ? policy[start..] : policy[start..end];
    }

    [Fact]
    public void BuildHtml_EveryCallGeneratesFreshNonce()
    {
        string first = GraphWindow.BuildHtml(MinimalSnapshot, isDarkMode: false);
        string second = GraphWindow.BuildHtml(MinimalSnapshot, isDarkMode: false);

        string firstNonce = ExtractNonce(first);
        string secondNonce = ExtractNonce(second);

        Assert.False(string.IsNullOrEmpty(firstNonce));
        Assert.False(string.IsNullOrEmpty(secondNonce));
        Assert.NotEqual(firstNonce, secondNonce);
    }

    [Fact]
    public void BuildHtml_EmbedsPayloadInsideJsonDataBlock()
    {
        string html = GraphWindow.BuildHtml(MinimalSnapshot, isDarkMode: false);

        Assert.Contains("<script type=\"application/json\" id=\"graph-payload\">", html, StringComparison.Ordinal);
        Assert.Contains(MinimalSnapshot, html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildHtml_OnNullSnapshot_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => GraphWindow.BuildHtml(null!, isDarkMode: false));
    }

    [Fact]
    public void BuildHtml_ScriptTagsCarryNonceAttribute()
    {
        string html = GraphWindow.BuildHtml(MinimalSnapshot, isDarkMode: false);

        string nonce = ExtractNonce(html);
        Assert.Contains($"<script nonce=\"{nonce}\">", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "light")]
    [InlineData(true, "dark")]
    public void BuildHtml_MarksTheRequestedAppearance(bool isDarkMode, string erwartet)
    {
        // Die Zeichenfläche stand fest auf Dunkel — im hellen Erscheinungsbild ein
        // schwarzes Feld neben lauter hellen Flächen. Jetzt trägt das Dokument selbst,
        // welche Belegung gilt, und das Stylesheet setzt die Farben dazu.
        string html = GraphWindow.BuildHtml(MinimalSnapshot, isDarkMode);

        Assert.Contains($"data-theme=\"{erwartet}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__THEME__", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildHtml_LeavesNoColourFixedInTheDrawingCode()
    {
        // Wer eine Farbe wieder fest in das Zeichnen schreibt, hebelt die Belegung aus.
        string html = GraphWindow.BuildHtml(MinimalSnapshot, isDarkMode: false);

        Assert.Contains("--graph-canvas", html, StringComparison.Ordinal);
        Assert.Contains("palette.canvas", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ctx.fillStyle = \"#", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ctx.strokeStyle = \"#", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildHtml_RedrawsAfterAResize()
    {
        // Ohne diesen Aufruf blieb die Fläche leer, sobald das Fenster nach dem Auslaufen
        // der Simulation vergrößert wurde — das Setzen der Größe leert sie.
        string html = GraphWindow.BuildHtml(MinimalSnapshot, isDarkMode: false);

        int resizeStart = html.IndexOf("function resize()", StringComparison.Ordinal);
        Assert.True(resizeStart > 0, "Der Größenwechsel wird nicht mehr behandelt.");
        int resizeEnd = html.IndexOf('}', resizeStart);
        Assert.Contains("draw();", html[resizeStart..resizeEnd], StringComparison.Ordinal);
    }

    private static string ExtractNonce(string html)
    {
        Match match = Regex.Match(html, "'nonce-([A-Za-z0-9+/=]+)'", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }
}
