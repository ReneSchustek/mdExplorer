using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

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
    private const string OpenMark = "<mark>";
    private const string CloseMark = "</mark>";

    /// <summary>Brush für Treffer-Hervorhebungen (Light- und Dark-Mode tragend, ähnliches Gelb).</summary>
    public Brush HighlightBackground { get; set; } = new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xC5));

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

    private IEnumerable<Inline> BuildInlines(string snippet)
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
            string highlighted = snippet[contentStart..closeIndex];
            yield return new Run(highlighted) { Background = HighlightBackground };
            cursor = closeIndex + CloseMark.Length;
        }
    }
}
