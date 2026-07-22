using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;

namespace MdExplorer.App.Tests.Services;

/// <summary>Unit-Tests des <see cref="PreviewHtmlBuilder"/>.</summary>
public sealed class PreviewHtmlBuilderTests
{
    [Fact]
    public void Build_OnNonEmptyBody_ReturnsDoctypeAndCsp()
    {
        PreviewHtmlBuilder sut = new(new FakeThemeProvider(isDarkMode: false));

        string html = sut.Build("<p>Hallo Welt</p>");

        Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
        Assert.Contains("Content-Security-Policy", html, StringComparison.Ordinal);
        Assert.Contains("<p>Hallo Welt</p>", html, StringComparison.Ordinal);
        Assert.Contains("</body></html>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OnLightTheme_EmbedsLightCss()
    {
        PreviewHtmlBuilder sut = new(new FakeThemeProvider(isDarkMode: false));

        string html = sut.Build("<p>Body</p>");

        Assert.Contains("Light Theme", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Dark Theme", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OnDarkTheme_EmbedsDarkCss()
    {
        PreviewHtmlBuilder sut = new(new FakeThemeProvider(isDarkMode: true));

        string html = sut.Build("<p>Body</p>");

        Assert.Contains("Dark Theme", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Light Theme", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildEmpty_ProducesValidDocumentWithEmptyBody()
    {
        PreviewHtmlBuilder sut = new(new FakeThemeProvider(isDarkMode: false));

        string html = sut.BuildEmpty();

        Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
        Assert.Contains("<body></body>", html, StringComparison.Ordinal);
        Assert.Contains("Content-Security-Policy", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ContainsExpectedCspDirectives()
    {
        PreviewHtmlBuilder sut = new(new FakeThemeProvider(isDarkMode: false));

        string html = sut.Build("<p>Body</p>");

        Assert.Contains("default-src 'none'", html, StringComparison.Ordinal);
        Assert.Contains("script-src 'none'", html, StringComparison.Ordinal);
        Assert.Contains("img-src 'self' data:", html, StringComparison.Ordinal);
        Assert.Contains("style-src 'self' 'unsafe-inline'", html, StringComparison.Ordinal);
    }
}
