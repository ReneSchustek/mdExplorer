using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MdExplorer.App.Converters;
using MdExplorer.Core.Abstractions;

namespace MdExplorer.App.Tests.Views;

/// <summary>
/// Hält fest, dass die Trefferhervorhebung der Suche der Farbbelegung folgt.
/// </summary>
/// <remarks>
/// Setzte der Wandler einen festen hellen Gelbton und keine Vordergrundfarbe, stünde im
/// dunklen Erscheinungsbild helle Schrift auf hellem Grund. Unlesbar wäre ausgerechnet die
/// Stelle, wegen der man hinsieht.
/// </remarks>
public sealed class HighlightToInlinesConverterTests
{
    [Fact]
    public void SnippetWithoutMarkerYieldsSinglePlainRun() => StaRunner.Run(() =>
    {
        TextBlock block = Convert("Text ohne Treffer");

        Inline single = Assert.Single(block.Inlines);
        Run run = Assert.IsType<Run>(single);
        Assert.Equal("Text ohne Treffer", run.Text);
    });

    [Fact]
    public void SnippetWithMarkerSplitsIntoThreeRuns() => StaRunner.Run(() =>
    {
        TextBlock block = Convert("vor " + SnippetMarkers.Open + "Treffer" + SnippetMarkers.Close + " nach");

        Assert.Equal(3, block.Inlines.Count);
        Assert.Equal(
            ["vor ", "Treffer", " nach"],
            block.Inlines.OfType<Run>().Select(run => run.Text));
    });

    /// <remarks>
    /// Beide Farben zählen. Ein gesetzter Hintergrund ohne gesetzten Vordergrund ist der
    /// Fehler, den dieser Test verhindert.
    /// </remarks>
    [Fact]
    public void HighlightedRunBindsBothColorsToThePalette() => StaRunner.Run(() =>
    {
        Run run = HighlightToInlinesConverter.BuildHighlightRun("Treffer");

        Assert.NotEqual(
            DependencyProperty.UnsetValue,
            run.ReadLocalValue(TextElement.BackgroundProperty));
        Assert.NotEqual(
            DependencyProperty.UnsetValue,
            run.ReadLocalValue(TextElement.ForegroundProperty));
    });

    [Fact]
    public void HighlightKeysMatchThePaletteEntries()
    {
        Assert.Equal("SearchHighlightBackgroundBrush", HighlightToInlinesConverter.HighlightBackgroundKey);
        Assert.Equal("SearchHighlightForegroundBrush", HighlightToInlinesConverter.HighlightForegroundKey);
    }

    /// <remarks>
    /// Ein Schnipsel, dem das schließende Gegenstück fehlt, kommt aus einer abgeschnittenen
    /// Ausgabe von FTS5. Er darf den Aufbau der Liste nicht abbrechen.
    /// </remarks>
    [Fact]
    public void UnclosedMarkerDoesNotBreakTheBuild() => StaRunner.Run(() =>
    {
        TextBlock block = Convert("vor " + SnippetMarkers.Open + "Rest ohne Ende");

        Assert.Equal(2, block.Inlines.Count);
        Assert.Equal(["vor ", "Rest ohne Ende"], block.Inlines.OfType<Run>().Select(run => run.Text));
    });

    [Fact]
    public void EmptyAndForeignInputYieldAnEmptyBlock() => StaRunner.Run(() =>
    {
        Assert.Empty(Convert(string.Empty).Inlines);
        Assert.Empty(Convert(42).Inlines);
    });

    [Fact]
    public void ConvertBackIsNotSupported() => StaRunner.Run(() =>
    {
        HighlightToInlinesConverter converter = new();

        _ = Assert.Throws<NotSupportedException>(
            () => converter.ConvertBack("x", typeof(string), null, CultureInfo.InvariantCulture));
    });

    private static TextBlock Convert(object? value)
    {
        HighlightToInlinesConverter converter = new();

        return Assert.IsType<TextBlock>(
            converter.Convert(value, typeof(object), null, CultureInfo.InvariantCulture));
    }
}
