using System.Windows;
using System.Windows.Media;

namespace MdExplorer.App.Tests.Views;

/// <summary>
/// Die Oberfläche zeichnet mit den Schriften aus der Programmdatei, nicht mit einer
/// installierten. Auf einem Rechner ohne Roboto fiele sie sonst still auf Segoe zurück.
/// </summary>
/// <remarks>
/// Für jedes Gewicht, das die Ansichten setzen, ein eigener Schnitt: WPF wertet die
/// Gewichtsachse einer variablen Schrift nicht aus und fettete SemiBold sonst künstlich nach.
/// </remarks>
public sealed class EmbeddedFontTests
{
    [Theory]
    [InlineData("AppFontFamily", "Normal", "roboto-flex-regular.ttf")]
    [InlineData("AppFontFamily", "SemiBold", "roboto-flex-semibold.ttf")]
    [InlineData("MonospaceFontFamily", "Normal", "roboto-mono-regular.ttf")]
    [InlineData("MonospaceFontFamily", "SemiBold", "roboto-mono-semibold.ttf")]
    public void TheFontToken_ResolvesToTheFileInTheProgram(string token, string weight, string expectedFile) => StaRunner.Run(() =>
    {
        ApplicationResources.Use(() =>
        {
            FontFamily family = Assert.IsType<FontFamily>(ApplicationResources.Resource(token));
            FontWeight fontWeight = (FontWeight)new FontWeightConverter().ConvertFromInvariantString(weight)!;
            Typeface typeface = new(family, FontStyles.Normal, fontWeight, FontStretches.Normal);

            Assert.True(typeface.TryGetGlyphTypeface(out GlyphTypeface glyphs), $"{token} liefert keine Schrift.");
            Assert.Equal("pack", glyphs.FontUri.Scheme);
            Assert.EndsWith(expectedFile, glyphs.FontUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(StyleSimulations.None, glyphs.StyleSimulations);
        });
    });
}
