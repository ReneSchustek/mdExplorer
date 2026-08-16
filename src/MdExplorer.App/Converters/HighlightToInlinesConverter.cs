using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

namespace MdExplorer.App.Converters;

/// <summary>
/// Wandelt einen FTS5-Snippet-String (mit <c>&lt;mark&gt;…&lt;/mark&gt;</c>-Markern)
/// in ein <see cref="TextBlock"/> mit hervorgehobenen <see cref="Run"/>-Elementen.
/// </summary>
/// <remarks>
/// Bewusst kein <see cref="IValueConverter"/>, das <see cref="InlineCollection"/> liefert —
/// WPF erlaubt das Binding nur über <c>TextBlock</c>-Substitution. Diese Klasse implementiert
/// <see cref="IValueConverter"/>, der direkt ein <see cref="TextBlock"/> baut.
/// </remarks>
internal sealed class HighlightToInlinesConverter : IValueConverter
{
    /// <summary>Schlüssel des Hintergrundpinsels der Trefferstelle in beiden Belegungen.</summary>
    public const string HighlightBackgroundKey = "SearchHighlightBackgroundBrush";

    /// <summary>Schlüssel des Vordergrundpinsels der Trefferstelle in beiden Belegungen.</summary>
    public const string HighlightForegroundKey = "SearchHighlightForegroundBrush";

    private const string OpenMark = "<mark>";
    private const string CloseMark = "</mark>";

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        TextBlock textBlock = new()
        {
            TextWrapping = TextWrapping.Wrap,
        };
        if (value is not string snippet || snippet.Length == 0)
        {
            return textBlock;
        }
        foreach (Inline inline in BuildInlines(snippet))
        {
            textBlock.Inlines.Add(inline);
        }
        return textBlock;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <summary>
    /// Baut den hervorgehobenen Abschnitt und bindet ihn an die Belegung.
    /// </summary>
    /// <remarks>
    /// Beide Farben, nicht nur die Fläche: Ohne gesetzten Vordergrund erbt der Abschnitt die
    /// Textfarbe der Umgebung — im dunklen Erscheinungsbild also helle Schrift auf hellem
    /// Grund, und die Trefferstelle ist genau die Stelle, die dann verschwindet.
    ///
    /// Über <see cref="FrameworkContentElement.SetResourceReference"/> und nicht über einen
    /// einmal gelesenen Pinsel: Der Verweis löst sich beim Tausch des Wörterbuchs neu auf,
    /// ein gelesener Wert bliebe in der alten Belegung stehen. Fehlt die Ressource — etwa im
    /// Test ohne laufende Anwendung —, bleibt die Eigenschaft ungesetzt und der Abschnitt
    /// erbt wie jeder andere Text.
    /// </remarks>
    internal static Run BuildHighlightRun(string text)
    {
        Run run = new(text);
        run.SetResourceReference(TextElement.BackgroundProperty, HighlightBackgroundKey);
        run.SetResourceReference(TextElement.ForegroundProperty, HighlightForegroundKey);
        return run;
    }

    private static IEnumerable<Inline> BuildInlines(string snippet)
    {
        int cursor = 0;
        while (cursor < snippet.Length)
        {
            int openIndex = snippet.IndexOf(OpenMark, cursor, StringComparison.Ordinal);
            if (openIndex < 0)
            {
                yield return new Run(snippet[cursor..]);
                yield break;
            }
            if (openIndex > cursor)
            {
                yield return new Run(snippet[cursor..openIndex]);
            }
            int contentStart = openIndex + OpenMark.Length;
            int closeIndex = snippet.IndexOf(CloseMark, contentStart, StringComparison.Ordinal);
            if (closeIndex < 0)
            {
                yield return new Run(snippet[contentStart..]);
                yield break;
            }
            yield return BuildHighlightRun(snippet[contentStart..closeIndex]);
            cursor = closeIndex + CloseMark.Length;
        }
    }
}
