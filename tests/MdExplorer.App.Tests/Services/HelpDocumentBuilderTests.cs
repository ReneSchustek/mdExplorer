using System.Text.RegularExpressions;
using MdExplorer.App.Localization;
using MdExplorer.App.Services.Help;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Das Dokument des Hilfefensters lässt nur Skripte und Stilblöcke mit der Nonce dieses Aufrufs
/// zu und folgt dem Erscheinungsbild der übrigen Oberfläche.
/// </summary>
public sealed partial class HelpDocumentBuilderTests
{
    private const string Handbook = "<h2 id=\"suche\">Suche</h2><p>Text</p>";

    [Theory]
    [InlineData(true, "dark")]
    [InlineData(false, "light")]
    public void Build_MarksTheEffectiveTheme(bool isDarkMode, string expected)
    {
        string html = HelpDocumentBuilder.Build(Handbook, isDarkMode);

        Assert.Contains($"<body data-theme=\"{expected}\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ScriptsAndStylesNeedTheNonceOfThisDocument()
    {
        string html = HelpDocumentBuilder.Build(Handbook, isDarkMode: false);

        string nonce = Nonce().Match(html).Groups["nonce"].Value;
        string policy = Policy().Match(html).Groups["policy"].Value;
        Assert.NotEmpty(nonce);
        Assert.Contains($"script-src 'nonce-{nonce}'", policy, StringComparison.Ordinal);
        Assert.Contains($"style-src 'nonce-{nonce}'", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("script-src 'unsafe-inline'", policy, StringComparison.Ordinal);
        Assert.Contains($"<script nonce=\"{nonce}\">", html, StringComparison.Ordinal);
        Assert.Contains($"<style nonce=\"{nonce}\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_EveryCallGetsAFreshNonce()
    {
        string first = Nonce().Match(HelpDocumentBuilder.Build(Handbook, isDarkMode: false)).Groups["nonce"].Value;
        string second = Nonce().Match(HelpDocumentBuilder.Build(Handbook, isDarkMode: false)).Groups["nonce"].Value;

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Build_LeavesPlaceholdersInTheHandbookAlone()
    {
        // Das Handbuch kommt zuletzt; ein Platzhalter darin bleibt Text.
        const string handbook = "<p>__INLINE_JS__ und __NONCE__</p>";

        string html = HelpDocumentBuilder.Build(handbook, isDarkMode: false);

        Assert.Contains(handbook, html, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TakesItsTitleFromTheResources()
    {
        string html = HelpDocumentBuilder.Build(Handbook, isDarkMode: false);

        Assert.Contains("<title>" + UiText.Get("Help_Title") + "</title>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__", html.Replace(Handbook, string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [GeneratedRegex("'nonce-(?<nonce>[A-Za-z0-9+/=]+)'")]
    private static partial Regex Nonce();

    [GeneratedRegex("http-equiv=\"Content-Security-Policy\" content=\"(?<policy>[^\"]+)\"")]
    private static partial Regex Policy();
}
