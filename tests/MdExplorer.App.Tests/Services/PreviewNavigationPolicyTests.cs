using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Die Vorschau zeigt nur das eigene Dokument. Ein Klick auf einen Verweis ins Netz darf die
/// fremde Seite nicht in die Fläche laden: Dort fehlen Adressleiste und die Vorkehrungen eines
/// Browsers, und der Nutzer sähe nicht, wo er ist.
/// </summary>
public sealed class PreviewNavigationPolicyTests
{
    [Theory]
    [InlineData("about:blank")]
    [InlineData("data:text/html;charset=utf-8;base64,PGh0bWw+")]
    public void Classify_TheOwnDocumentLoad_IsShownInPlace(string address)
    {
        Assert.Equal(PreviewNavigationKind.Document, PreviewNavigationPolicy.Classify(address, isUserInitiated: false));
    }

    [Fact]
    public void Classify_AClickOnADataLink_IsBlocked()
    {
        // Ein data:-Verweis im Dokument könnte eine beliebige Seite in die Fläche legen.
        Assert.Equal(PreviewNavigationKind.Blocked, PreviewNavigationPolicy.Classify("data:text/html,<h1>x</h1>", isUserInitiated: true));
    }

    [Theory]
    [InlineData("https://example.org/seite")]
    [InlineData("http://example.org")]
    [InlineData("mailto:jemand@example.org")]
    [InlineData("HTTPS://EXAMPLE.ORG")]
    public void Classify_ALinkIntoTheNet_GoesToTheDefaultProgram(string address)
    {
        Assert.Equal(PreviewNavigationKind.External, PreviewNavigationPolicy.Classify(address, isUserInitiated: true));
    }

    [Fact]
    public void Classify_AWikiLink_IsHandledByTheApplication()
    {
        Assert.Equal(PreviewNavigationKind.WikiLink, PreviewNavigationPolicy.Classify("mdexplorer://Projekt%20X", isUserInitiated: true));
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:privacy")]
    [InlineData("ftp://example.org/datei")]
    [InlineData("about:blank")]
    [InlineData("")]
    [InlineData("kein uri")]
    public void Classify_EverythingElseAUserClicks_IsBlocked(string address)
    {
        Assert.Equal(PreviewNavigationKind.Blocked, PreviewNavigationPolicy.Classify(address, isUserInitiated: true));
    }

    [Fact]
    public void Classify_AnExternalNavigationTheUserDidNotStart_IsBlocked()
    {
        // Eine Weiterleitung oder ein Meta-Refresh soll nicht unbemerkt den Browser öffnen.
        Assert.Equal(PreviewNavigationKind.Blocked, PreviewNavigationPolicy.Classify("https://example.org", isUserInitiated: false));
    }

    [Theory]
    [InlineData("mdexplorer://Projekt%20X", "Projekt X")]
    [InlineData("mdexplorer:Notiz", "Notiz")]
    [InlineData("MDEXPLORER:///Ordner/Notiz", "Ordner/Notiz")]
    public void WikiLinkTarget_DecodesTheTarget(string address, string expected)
    {
        Assert.Equal(expected, PreviewNavigationPolicy.WikiLinkTarget(address), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("mdexplorer://")]
    [InlineData("mdexplorer:%20")]
    public void WikiLinkTarget_WithoutATarget_ReturnsNull(string address)
    {
        Assert.Null(PreviewNavigationPolicy.WikiLinkTarget(address));
    }
}
