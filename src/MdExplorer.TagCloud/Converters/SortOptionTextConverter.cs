using System.Globalization;
using System.Windows.Data;
using MdExplorer.TagCloud.Localization;
using MdExplorer.TagCloud.Models;

namespace MdExplorer.TagCloud.Converters;

/// <summary>
/// Zeigt eine <see cref="TagCloudSortOption"/> mit ihrem Anzeigetext aus der Ressourcendatei statt
/// mit dem Namen des Enum-Werts.
/// </summary>
/// <remarks>
/// Die Schlüssel stehen ausgeschrieben da, damit der Ressourcen-Wächter sie findet; ein neuer Wert
/// ohne Eintrag fällt im Test <c>SortOptionTextConverterTests</c> auf.
/// </remarks>
public sealed class SortOptionTextConverter : IValueConverter
{
    /// <summary>Liefert den Anzeigetext eines Werts.</summary>
    /// <param name="option">Der Wert.</param>
    /// <returns>Der Text aus der Ressourcendatei.</returns>
    public static string TextOf(TagCloudSortOption option) => option switch
    {
        TagCloudSortOption.Frequency => UiText.Get("TagCloud_Sort_Frequency"),
        TagCloudSortOption.Alphabetical => UiText.Get("TagCloud_Sort_Alphabetical"),
        TagCloudSortOption.RecentlyUsed => UiText.Get("TagCloud_Sort_RecentlyUsed"),
        _ => throw new ArgumentOutOfRangeException(nameof(option), option, "Unbekannte Sortierung."),
    };

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TagCloudSortOption option ? TextOf(option) : string.Empty;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("SortOptionTextConverter ist nur in eine Richtung definiert.");
}
