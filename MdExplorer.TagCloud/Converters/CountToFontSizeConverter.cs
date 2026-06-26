using System.Globalization;
using System.Windows.Data;
using MdExplorer.TagCloud.Services;

namespace MdExplorer.TagCloud.Converters;

/// <summary>
/// WPF-<see cref="IMultiValueConverter"/>, der einen Tag-Count zusammen mit dem aktuellen
/// Snapshot-Minimum/-Maximum in eine DIP-Schriftgröße umsetzt. Die Berechnung delegiert an
/// <see cref="TagFontSizeCalculator"/>. Schriftgrößen-Grenzen sind als Properties konfigurierbar
/// und werden i. d. R. aus den Tag-Cloud-Options gespeist.
/// </summary>
public sealed class CountToFontSizeConverter : IMultiValueConverter
{
    /// <summary>Index 0 — der Tag-Count.</summary>
    public const int CountIndex = 0;

    /// <summary>Index 1 — Snapshot-Minimum.</summary>
    public const int MinCountIndex = 1;

    /// <summary>Index 2 — Snapshot-Maximum.</summary>
    public const int MaxCountIndex = 2;

    private const int ExpectedValueCount = 3;

    /// <summary>Untere Grenze der DIP-Schriftgröße (Default 10).</summary>
    public double MinFontSize { get; set; } = 10.0;

    /// <summary>Obere Grenze der DIP-Schriftgröße (Default 26).</summary>
    public double MaxFontSize { get; set; } = 26.0;

    /// <inheritdoc />
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length < ExpectedValueCount)
        {
            return MinFontSize;
        }
        if (!TryReadInt(values[CountIndex], out int count) ||
            !TryReadInt(values[MinCountIndex], out int minCount) ||
            !TryReadInt(values[MaxCountIndex], out int maxCount))
        {
            return MinFontSize;
        }
        if (minCount < 1 || maxCount < minCount)
        {
            return MinFontSize;
        }
        return TagFontSizeCalculator.Compute(count, minCount, maxCount, MinFontSize, MaxFontSize);
    }

    /// <inheritdoc />
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("CountToFontSizeConverter ist nur in eine Richtung definiert.");

    private static bool TryReadInt(object? candidate, out int parsed)
    {
        switch (candidate)
        {
            case int intValue:
                parsed = intValue;
                return true;
            case long longValue:
                parsed = checked((int)longValue);
                return true;
            default:
                parsed = 0;
                return false;
        }
    }
}
