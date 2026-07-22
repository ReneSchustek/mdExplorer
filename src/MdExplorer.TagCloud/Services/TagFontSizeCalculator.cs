namespace MdExplorer.TagCloud.Services;

/// <summary>
/// Reine, allokationsfreie Logarithmus-Skalierung von Tag-Häufigkeiten auf eine DIP-Schriftgröße.
/// Außerhalb des Intervalls <c>[minCount, maxCount]</c> wird geklemmt, im Sonderfall
/// <c>minCount == maxCount</c> wird die Min-Schriftgröße geliefert (keine Spreizung sichtbar).
/// </summary>
public static class TagFontSizeCalculator
{
    /// <summary>
    /// Berechnet die Schriftgröße zu einem Tag-Count.
    /// Formel: <c>min + (max - min) * (log10(count) - log10(minCount)) / (log10(maxCount) - log10(minCount))</c>.
    /// </summary>
    /// <param name="count">Häufigkeit des Tags (muss &gt;= 0 sein).</param>
    /// <param name="minCount">Niedrigste Häufigkeit im Snapshot (muss &gt;= 1 sein).</param>
    /// <param name="maxCount">Höchste Häufigkeit im Snapshot (muss &gt;= <paramref name="minCount"/>).</param>
    /// <param name="minFontSize">Untere Grenze der DIP-Schriftgröße.</param>
    /// <param name="maxFontSize">Obere Grenze der DIP-Schriftgröße.</param>
    public static double Compute(int count, int minCount, int maxCount, double minFontSize, double maxFontSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfLessThan(minCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, minCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minFontSize);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFontSize, minFontSize);

        if (count <= minCount)
        {
            return minFontSize;
        }
        if (count >= maxCount)
        {
            return maxFontSize;
        }
        if (minCount == maxCount)
        {
            return minFontSize;
        }

        double logMin = Math.Log10(minCount);
        double logMax = Math.Log10(maxCount);
        double logSpan = logMax - logMin;
        double normalized = (Math.Log10(count) - logMin) / logSpan;
        return minFontSize + ((maxFontSize - minFontSize) * normalized);
    }
}
