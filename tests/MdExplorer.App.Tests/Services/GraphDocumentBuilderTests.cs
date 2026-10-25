using System.Text.RegularExpressions;
using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Das Graph-Fenster lässt nur Skripte mit der Nonce dieses Aufrufs laufen, nie
/// <c>'unsafe-inline'</c>, und der Snapshot liegt in einem nicht ausführbaren
/// <c>application/json</c>-Block.
/// </summary>
public sealed class GraphDocumentBuilderTests
{
    private const string MinimalSnapshot = """{"nodes":[],"edges":[]}""";

    [Fact]
    public void Build_ProducesScriptSrcWithNonceAndWithoutUnsafeInline()
    {
        string html = GraphDocumentBuilder.Build(MinimalSnapshot, isDarkMode: false);

        Match cspMatch = Regex.Match(
            html,
            "<meta http-equiv=\"Content-Security-Policy\" content=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);
        Assert.True(cspMatch.Success);
        string policy = cspMatch.Groups[1].Value;
        string scriptSrc = ExtractDirective(policy, "script-src");
        Assert.Contains("'nonce-", scriptSrc, StringComparison.Ordinal);
        // Skripte werden ausschließlich über die Nonce autorisiert, kein 'unsafe-inline' zulässig.
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
    public void Build_EveryCallGeneratesFreshNonce()
    {
        string first = GraphDocumentBuilder.Build(MinimalSnapshot, isDarkMode: false);
        string second = GraphDocumentBuilder.Build(MinimalSnapshot, isDarkMode: false);

        string firstNonce = ExtractNonce(first);
        string secondNonce = ExtractNonce(second);

        Assert.False(string.IsNullOrEmpty(firstNonce));
        Assert.False(string.IsNullOrEmpty(secondNonce));
        Assert.NotEqual(firstNonce, secondNonce);
    }

    [Fact]
    public void Build_EmbedsPayloadInsideJsonDataBlock()
    {
        string html = GraphDocumentBuilder.Build(MinimalSnapshot, isDarkMode: false);

        Assert.Contains("<script type=\"application/json\" id=\"graph-payload\">", html, StringComparison.Ordinal);
        Assert.Contains(MinimalSnapshot, html, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OnNullSnapshot_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => GraphDocumentBuilder.Build(null!, isDarkMode: false));
    }

    [Fact]
    public void Build_ScriptTagsCarryNonceAttribute()
    {
        string html = GraphDocumentBuilder.Build(MinimalSnapshot, isDarkMode: false);

        string nonce = ExtractNonce(html);
        Assert.Contains($"<script nonce=\"{nonce}\">", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "light")]
    [InlineData(true, "dark")]
    public void Build_MarksTheRequestedAppearance(bool isDarkMode, string expected)
    {
        // Das Dokument trägt selbst, welche Belegung gilt, und das Stylesheet setzt die
        // Farben dazu. Eine fest dunkle Zeichenfläche wäre im hellen Erscheinungsbild ein
        // schwarzes Feld neben lauter hellen Flächen.
        string html = GraphDocumentBuilder.Build(MinimalSnapshot, isDarkMode);

        Assert.Contains($"data-theme=\"{expected}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__THEME__", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_LeavesNoColourFixedInTheDrawingCode()
    {
        // Wer eine Farbe wieder fest in das Zeichnen schreibt, hebelt die Belegung aus.
        string html = GraphDocumentBuilder.Build(MinimalSnapshot, isDarkMode: false);

        Assert.Contains("--graph-canvas", html, StringComparison.Ordinal);
        Assert.Contains("palette.canvas", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ctx.fillStyle = \"#", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ctx.strokeStyle = \"#", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RedrawsAfterAResize()
    {
        // Das Setzen der Größe leert die Zeichenfläche. Ohne neues Zeichnen bliebe sie leer,
        // sobald das Fenster nach dem Auslaufen der Simulation vergrößert wird.
        string html = GraphDocumentBuilder.Build(MinimalSnapshot, isDarkMode: false);

        int resizeStart = html.IndexOf("function resize(graph)", StringComparison.Ordinal);
        Assert.True(resizeStart > 0, "Der Größenwechsel wird nicht mehr behandelt.");
        int resizeEnd = html.IndexOf('}', resizeStart);
        Assert.Contains("draw(graph);", html[resizeStart..resizeEnd], StringComparison.Ordinal);
    }

    [Fact]
    public void Build_PayloadCannotCloseTheDataBlock()
    {
        // Ein Titel aus dem Bestand darf das Datenfeld nicht beenden: Dahinter stünde sonst
        // Markup, das der Browser ausführt.
        const string snapshot = """{"nodes":[{"id":1,"title":"</script><script>alert(1)</script>"}],"edges":[]}""";

        string html = GraphDocumentBuilder.Build(snapshot, isDarkMode: false);

        int dataStart = html.IndexOf("id=\"graph-payload\">", StringComparison.Ordinal);
        int dataEnd = html.IndexOf("</script>", dataStart, StringComparison.Ordinal);
        string data = html[dataStart..dataEnd];
        Assert.Contains(@"\u003c/script>", data, StringComparison.Ordinal);
        Assert.DoesNotContain("alert(1)</script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_StylesNeedTheNonceToo()
    {
        string html = GraphDocumentBuilder.Build(MinimalSnapshot, isDarkMode: false);

        string styleSrc = ExtractDirective(PolicyOf(html), "style-src");
        string nonce = ExtractNonce(html);
        Assert.Contains($"'nonce-{nonce}'", styleSrc, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-inline'", styleSrc, StringComparison.Ordinal);
        Assert.Contains($"<style nonce=\"{nonce}\">", html, StringComparison.Ordinal);
    }

    private static string PolicyOf(string html) =>
        Regex.Match(html, "<meta http-equiv=\"Content-Security-Policy\" content=\"([^\"]+)\"", RegexOptions.CultureInvariant).Groups[1].Value;

    private static string ExtractNonce(string html)
    {
        Match match = Regex.Match(html, "'nonce-([A-Za-z0-9+/=]+)'", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }
}
