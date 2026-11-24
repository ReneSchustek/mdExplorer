using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using MdExplorer.App.Services;
using MdExplorer.App.Tests.Views;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Im Windows-Kontrastmodus trägt die Oberfläche die Farben, die der Nutzer in Windows gewählt
/// hat, und zwar so, dass jeder Text auf einer Fläche steht, für die Windows den Kontrast zusichert.
/// </summary>
/// <remarks>
/// Windows sichert Kontrast nur für seine Paare zu: Fenstertext auf Fenster, Hervorhebungstext
/// auf Hervorhebung, ausgegrauter Text auf Fenster. Eine ausgewählte Listenzeile mit grauem
/// Pfad darunter darf deshalb nicht die Hervorhebung als Fläche bekommen; die Auswahl zeigt im
/// Kontrastmodus ein Rand.
/// </remarks>
public sealed class HighContrastPaletteTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Create_DefinesExactlyTheNamedKeysOfThePalettes() => StaRunner.Run(() =>
    {
        ResourceDictionary sut = HighContrastPalette.Create();

        string[] keys = [.. sut.Keys.OfType<string>().Order(StringComparer.Ordinal)];
        Assert.Equal(NamedKeysOf("Light.xaml"), keys);
    });

    [Theory]
    [InlineData("AppBackgroundBrush")]
    [InlineData("SurfaceHoverBrush")]
    [InlineData("SelectionBackgroundBrush")]
    [InlineData("InputBackgroundBrush")]
    [InlineData("NoticeWarningBackgroundBrush")]
    [InlineData("TagBackgroundBrush")]
    public void SurfacesThatCarryText_TakeTheWindowColor(string key) => StaRunner.Run(
        () => Assert.Equal(SystemColors.WindowColor, ColorOf(key)));

    [Theory]
    [InlineData("TextPrimaryBrush")]
    [InlineData("TextSecondaryBrush")]
    [InlineData("TextTertiaryBrush")]
    [InlineData("TagForegroundBrush")]
    [InlineData("NoticeWarningForegroundBrush")]
    [InlineData("StatusBarErrorBrush")]
    public void TextOnThoseSurfaces_TakesTheWindowTextColor(string key) => StaRunner.Run(
        () => Assert.Equal(SystemColors.WindowTextColor, ColorOf(key)));

    [Theory]
    [InlineData("TextDisabledBrush")]
    [InlineData("ButtonForegroundDisabledBrush")]
    public void DisabledText_TakesTheGrayTextColor(string key) => StaRunner.Run(
        () => Assert.Equal(SystemColors.GrayTextColor, ColorOf(key)));

    [Theory]
    [InlineData("AccentPrimaryBrush", "TextOnAccentBrush")]
    [InlineData("SearchHighlightBackgroundBrush", "SearchHighlightForegroundBrush")]
    public void HighlightedSurfaces_ComeAsAPairWithTheHighlightTextColor(string surface, string text) => StaRunner.Run(() =>
    {
        Assert.Equal(SystemColors.HighlightColor, ColorOf(surface));
        Assert.Equal(SystemColors.HighlightTextColor, ColorOf(text));
    });

    [Fact]
    public void ASelection_IsShownByItsOutline() => StaRunner.Run(() =>
    {
        Assert.Equal(SystemColors.HighlightColor, ColorOf("SelectionBorderBrush"));
        Assert.Equal(SystemColors.HighlightColor, ColorOf("SelectionOutlineBrush"));
    });

    /// <remarks>
    /// Mitgelieferte Muster wie das des Baums färben ihre Auswahl selbst über die Systemfarbe der
    /// Hervorhebung; darauf steht dann ein fest gesetzter Fenstertext. Die Belegung stellt deshalb
    /// genau diese Systemfarben auf das Fensterpaar um und lässt alle übrigen Windows.
    /// </remarks>
    [Fact]
    public void Create_TurnsOnlyTheSelectionSystemColorsIntoTheWindowPair() => StaRunner.Run(() =>
    {
        ResourceDictionary sut = HighContrastPalette.Create();

        Assert.Equal(4, sut.Keys.OfType<ResourceKey>().Count());
        Assert.Equal(SystemColors.WindowColor, Assert.IsType<SolidColorBrush>(sut[SystemColors.HighlightBrushKey]).Color);
        Assert.Equal(SystemColors.WindowTextColor, Assert.IsType<SolidColorBrush>(sut[SystemColors.HighlightTextBrushKey]).Color);
        Assert.Equal(SystemColors.WindowColor, Assert.IsType<SolidColorBrush>(sut[SystemColors.InactiveSelectionHighlightBrushKey]).Color);
        Assert.Equal(SystemColors.WindowTextColor, Assert.IsType<SolidColorBrush>(sut[SystemColors.InactiveSelectionHighlightTextBrushKey]).Color);
    });

    private static Color ColorOf(string key) =>
        Assert.IsType<SolidColorBrush>(HighContrastPalette.Create()[key]).Color;

    private static string[] NamedKeysOf(string palette)
    {
        string path = Path.Combine(RepositoryPaths.Project("MdExplorer.App"), "Themes", palette);
        return
        [
            .. XDocument.Load(path).Root!.Elements()
                .Select(element => (string?)element.Attribute(Xaml + "Key"))
                .OfType<string>()
                .Where(key => !key.StartsWith('{'))
                .Order(StringComparer.Ordinal),
        ];
    }
}
