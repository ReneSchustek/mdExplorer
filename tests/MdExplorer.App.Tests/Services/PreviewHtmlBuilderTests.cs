using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.Core.Models;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Jede Vorschauseite trägt die strikte Content-Security-Policy und das Stylesheet des gültigen
/// Erscheinungsbilds. Fehlte die Richtlinie, liefe ein Skript aus einer fremden Markdown-Datei
/// in der Vorschau.
/// </summary>
public sealed class PreviewHtmlBuilderTests
{
    [Fact]
    public void Build_OnNonEmptyBody_ReturnsDoctypeAndCsp()
    {
        PreviewHtmlBuilder sut = new(new FakeThemeProvider(isDarkMode: false), new FakeSettingsService(AppSettings.Default));

        string html = sut.Build("<p>Hallo Welt</p>");

        Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
        Assert.Contains("Content-Security-Policy", html, StringComparison.Ordinal);
        Assert.Contains("<p>Hallo Welt</p>", html, StringComparison.Ordinal);
        Assert.Contains("</body></html>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "light")]
    [InlineData(true, "dark")]
    public void Build_MarksTheThemeForTheSharedStylesheet(bool isDarkMode, string expected)
    {
        PreviewHtmlBuilder sut = new(new FakeThemeProvider(isDarkMode), new FakeSettingsService(AppSettings.Default));

        string html = sut.Build("<p>Body</p>");

        Assert.Contains($"<html lang=\"de\" data-theme=\"{expected}\">", html, StringComparison.Ordinal);
        Assert.Contains(":root[data-theme=\"dark\"]", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithLinks_UnderlinesThem()
    {
        // Ein Verweis darf nicht allein an der Farbe erkennbar sein (WCAG 1.4.1).
        PreviewHtmlBuilder sut = new(new FakeThemeProvider(isDarkMode: false), new FakeSettingsService(AppSettings.Default));

        string html = sut.Build("<p>Body</p>");

        Assert.Contains("a { color: var(--preview-link); text-decoration: underline; }", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildEmpty_WithoutContent_ProducesAValidDocument()
    {
        PreviewHtmlBuilder sut = new(new FakeThemeProvider(isDarkMode: false), new FakeSettingsService(AppSettings.Default));

        string html = sut.BuildEmpty();

        Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
        Assert.Contains("<body></body>", html, StringComparison.Ordinal);
        Assert.Contains("Content-Security-Policy", html, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Die Schriftgröße ist im Einstellungsdialog bedienbar und im Handbuch beschrieben. Sie muss
    /// deshalb in der Vorschau ankommen und das Stylesheet übersteuern, das sonst eine feste Größe
    /// setzt.
    /// </remarks>
    [Theory]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(24)]
    public void Build_SetsTheFontSizeFromTheSettings(int fontSize)
    {
        AppSettings settings = AppSettings.Default with
        {
            Appearance = AppearanceSettings.Default with { PreviewFontSize = fontSize },
        };
        PreviewHtmlBuilder sut = new(new FakeThemeProvider(isDarkMode: false), new FakeSettingsService(settings));

        string html = sut.Build("<p>Body</p>");

        Assert.Contains($"body {{ font-size: {fontSize}px; }}</style>", html, StringComparison.Ordinal);
        _ = Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "font-size: [0-9]+px"));
    }

    /// <remarks>
    /// Die Vorschau ist ein fertiges Dokument und hört auf keine Einstellung. Was in ihren Aufbau
    /// eingeht, verlangt nach der Änderung einen Neuaufbau; alles andere nicht, sonst flackerte sie
    /// bei jedem Speichern im Einstellungsdialog.
    /// </remarks>
    [Theory]
    [InlineData("Theme", true)]
    [InlineData("PreviewFontSize", true)]
    [InlineData("LoadRemoteImagesInPreview", true)]
    [InlineData("ResultsPerPage", false)]
    [InlineData("SearchDebounceMs", false)]
    [InlineData("Roots", false)]
    public void DependsOn_NamesExactlyTheSettingsThatEnterThePreview(string field, bool expected)
    {
        AppSettings previous = AppSettings.Default;
        AppSettings current = field switch
        {
            "Theme" => previous with { Appearance = previous.Appearance with { Theme = AppTheme.Dark } },
            "PreviewFontSize" => previous with { Appearance = previous.Appearance with { PreviewFontSize = 20 } },
            "LoadRemoteImagesInPreview" => previous with { Behavior = previous.Behavior with { LoadRemoteImagesInPreview = true } },
            "ResultsPerPage" => previous with { Appearance = previous.Appearance with { ResultsPerPage = 100 } },
            "SearchDebounceMs" => previous with { Behavior = previous.Behavior with { SearchDebounceMs = 500 } },
            _ => previous with { Indexing = previous.Indexing with { Roots = [@"C:\Notizen"] } },
        };

        Assert.Equal(expected, PreviewHtmlBuilder.DependsOn(previous, current));
    }

    [Fact]
    public void Build_WithDefaultSettings_ContainsTheExpectedCspDirectives()
    {
        PreviewHtmlBuilder sut = new(new FakeThemeProvider(isDarkMode: false), new FakeSettingsService(AppSettings.Default));

        string html = sut.Build("<p>Body</p>");

        Assert.Contains("default-src 'none'", html, StringComparison.Ordinal);
        Assert.Contains("script-src 'none'", html, StringComparison.Ordinal);
        Assert.Contains("img-src 'self' data:", html, StringComparison.Ordinal);
        Assert.Contains("style-src 'self' 'unsafe-inline'", html, StringComparison.Ordinal);
    }
}
