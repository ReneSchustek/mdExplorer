namespace MdExplorer.App.Views;

/// <summary>
/// Passt die Breiten der linken Spalte und der Tag-Cloud an das Fenster an.
/// </summary>
/// <remarks>
/// Bei Platzmangel geben zuerst die linke Spalte, dann die Tag-Cloud bis zu ihren Mindestbreiten
/// nach. Ohne diese Rechnung behielte die linke Spalte ihre gespeicherte Breite, die Vorschau ihre
/// Mindestbreite, und was darüber hinausginge, schnitte das Fenster am rechten Rand ab: die halbe
/// Tag-Cloud samt ihrem ✕. Die Wunschbreiten bleiben dabei unberührt; wird das Fenster wieder
/// breiter, kehren die Spalten zu ihnen zurück.
/// </remarks>
internal static class ColumnFit
{
    /// <summary>Rechnet die Breiten aus.</summary>
    /// <param name="available">Breite des Spaltenbereichs.</param>
    /// <param name="reserved">Was unverrückbar Platz braucht: Trenner und Mindestbreite der Vorschau.</param>
    /// <param name="folder">Wunsch- und Mindestbreite der linken Spalte.</param>
    /// <param name="tagCloud">Wunsch- und Mindestbreite der Tag-Cloud; Wunsch 0, wenn sie verborgen ist.</param>
    /// <returns>Die Breiten, die gesetzt werden.</returns>
    public static ColumnWidths Fit(double available, double reserved, ColumnWish folder, ColumnWish tagCloud)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(tagCloud);

        double room = Math.Max(0.0, available - reserved);
        double folderWidth = Math.Max(folder.Minimum, Math.Min(folder.Wanted, room - tagCloud.Wanted));
        double tagCloudWidth = tagCloud.Wanted <= 0.0
            ? 0.0
            : Math.Max(tagCloud.Minimum, Math.Min(tagCloud.Wanted, room - folderWidth));
        return new ColumnWidths(folderWidth, tagCloudWidth);
    }
}

/// <summary>Wunsch- und Mindestbreite einer Spalte.</summary>
/// <param name="Wanted">Die Breite, die die Spalte bei genug Platz hat.</param>
/// <param name="Minimum">Schmaler wird sie nicht.</param>
internal sealed record ColumnWish(double Wanted, double Minimum);

/// <summary>Die ausgerechneten Breiten.</summary>
/// <param name="Folder">Linke Spalte.</param>
/// <param name="TagCloud">Tag-Cloud; 0, wenn sie verborgen ist.</param>
internal sealed record ColumnWidths(double Folder, double TagCloud);
