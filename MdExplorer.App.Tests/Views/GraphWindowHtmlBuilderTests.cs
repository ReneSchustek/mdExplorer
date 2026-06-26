using System.Text.RegularExpressions;
using MdExplorer.App.Views.Graph;

namespace MdExplorer.App.Tests.Views;

/// <summary>
/// Tests die HTML-Verpackung des Graph-Fensters. Pruefparameter: CSP-Strenge (kein
/// <c>'unsafe-inline'</c>), pro Aufruf neuer Nonce, Snapshot-JSON liegt im nicht-ausfuehrbaren
/// <c>application/json</c>-Datenblock und kann kein <c>&lt;/script&gt;</c> einschleusen.
/// </summary>
public sealed class GraphWindowHtmlBuilderTests
{
    private const string MinimalSnapshot = """{"nodes":[],"edges":[]}""";

    [Fact]
    public void BuildHtml_ProducesScriptSrcWithNonceAndWithoutUnsafeInline()
    {
        string html = GraphWindow.BuildHtml(MinimalSnapshot);

        Match cspMatch = Regex.Match(
            html,
            "<meta http-equiv=\"Content-Security-Policy\" content=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);
        Assert.True(cspMatch.Success);
        string policy = cspMatch.Groups[1].Value;
        string scriptSrc = ExtractDirective(policy, "script-src");
        Assert.Contains("'nonce-", scriptSrc, StringComparison.Ordinal);
        // Skripte werden ausschliesslich ueber die Nonce autorisiert — kein 'unsafe-inline' zulaessig.
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
        string first = GraphWindow.BuildHtml(MinimalSnapshot);
        string second = GraphWindow.BuildHtml(MinimalSnapshot);

        string firstNonce = ExtractNonce(first);
        string secondNonce = ExtractNonce(second);

        Assert.False(string.IsNullOrEmpty(firstNonce));
        Assert.False(string.IsNullOrEmpty(secondNonce));
        Assert.NotEqual(firstNonce, secondNonce);
    }

    [Fact]
    public void BuildHtml_EmbedsPayloadInsideJsonDataBlock()
    {
        string html = GraphWindow.BuildHtml(MinimalSnapshot);

        Assert.Contains("<script type=\"application/json\" id=\"graph-payload\">", html, StringComparison.Ordinal);
        Assert.Contains(MinimalSnapshot, html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildHtml_OnNullSnapshot_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => GraphWindow.BuildHtml(null!));
    }

    [Fact]
    public void BuildHtml_ScriptTagsCarryNonceAttribute()
    {
        string html = GraphWindow.BuildHtml(MinimalSnapshot);

        string nonce = ExtractNonce(html);
        Assert.Contains($"<script nonce=\"{nonce}\">", html, StringComparison.Ordinal);
    }

    private static string ExtractNonce(string html)
    {
        Match match = Regex.Match(html, "'nonce-([A-Za-z0-9+/=]+)'", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }
}
