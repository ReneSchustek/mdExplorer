using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MdExplorer.App.Converters;
using Xunit.Sdk;

namespace MdExplorer.App.Tests.Views;

/// <summary>
/// Hält fest, dass die Trefferhervorhebung der Suche der Farbbelegung folgt.
/// </summary>
/// <remarks>
/// Anlass ist ein Befund vom 16.08.2026: Der Wandler setzte einen festen hellen Gelbton und
/// keine Vordergrundfarbe. Im dunklen Erscheinungsbild stand damit helle Schrift auf hellem
/// Grund — unlesbar war ausgerechnet die Stelle, wegen der man hinsieht.
/// </remarks>
public sealed class HighlightToInlinesConverterTests
{
    [Fact]
    public void SnippetWithoutMarkerYieldsSinglePlainRun() => RunSta(() =>
    {
        TextBlock block = Convert("Text ohne Treffer");

        Inline single = Assert.Single(block.Inlines);
        Run run = Assert.IsType<Run>(single);
        Assert.Equal("Text ohne Treffer", run.Text);
    });

    [Fact]
    public void SnippetWithMarkerSplitsIntoThreeRuns() => RunSta(() =>
    {
        TextBlock block = Convert("vor <mark>Treffer</mark> nach");

        Assert.Equal(3, block.Inlines.Count);
        Assert.Equal(
            ["vor ", "Treffer", " nach"],
            block.Inlines.OfType<Run>().Select(run => run.Text));
    });

    /// <remarks>
    /// Der Kern des Befunds: <b>beide</b> Farben. Ein gesetzter Hintergrund ohne gesetzten
    /// Vordergrund ist genau der Fehler, den dieser Test verhindert.
    /// </remarks>
    [Fact]
    public void HighlightedRunBindsBothColorsToThePalette() => RunSta(() =>
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
    public void UnclosedMarkerDoesNotBreakTheBuild() => RunSta(() =>
    {
        TextBlock block = Convert("vor <mark>Rest ohne Ende");

        Assert.Equal(2, block.Inlines.Count);
        Assert.Equal(["vor ", "Rest ohne Ende"], block.Inlines.OfType<Run>().Select(run => run.Text));
    });

    [Fact]
    public void EmptyAndForeignInputYieldAnEmptyBlock() => RunSta(() =>
    {
        Assert.Empty(Convert(string.Empty).Inlines);
        Assert.Empty(Convert(42).Inlines);
    });

    [Fact]
    public void ConvertBackIsNotSupported() => RunSta(() =>
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

    /// <summary>
    /// Führt die Prüfung auf einem STA-Thread aus.
    /// </summary>
    /// <remarks>
    /// <see cref="TextBlock"/> ist ein <see cref="FrameworkElement"/> und verlangt beim
    /// Erzeugen einen STA-Thread; der Testläufer stellt einen MTA-Thread. Und weil ein
    /// WPF-Objekt seinem erzeugenden Thread gehört, muss auch die Zusicherung dort laufen —
    /// deshalb die ganze Prüfung und nicht nur der Aufbau. Ein eigener Thread ist billiger
    /// als eine Sonderregel für die gesamte Suite.
    /// </remarks>
    private static void RunSta(Action assertion)
    {
        ExceptionDispatchInfo? failure = null;

        Thread thread = new(() =>
        {
            try
            {
                assertion();
            }
            catch (Exception exception) when (exception is XunitException or InvalidOperationException)
            {
                // Eine fehlgeschlagene Zusicherung und ein Thread-Verstoß sind das, was hier
                // auftreten kann. Beides gehört unverfälscht in den Testbericht, deshalb wird
                // es mit Ursprungs-Stapel weitergereicht statt neu geworfen.
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure?.Throw();
    }
}
