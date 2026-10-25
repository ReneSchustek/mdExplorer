namespace MdExplorer.App.ViewModels;

/// <summary>Der Ordneranteil eines Pfads relativ zur Wurzel, wie Liste und Bezüge ihn zeigen.</summary>
internal static class RelativeFolder
{
    /// <summary>
    /// Trennt den Ordneranteil eines relativen Pfads ab.
    /// </summary>
    /// <remarks>
    /// Beide Trennzeichen zählen: Der Index schreibt je nach Herkunft den einen oder den
    /// anderen, und ein Filter, der nur eines kennt, greift dann bei der Hälfte nicht. Gezeigt
    /// wird der Schrägstrich, unabhängig davon, was gespeichert ist.
    /// </remarks>
    /// <param name="relativePath">Pfad relativ zur Wurzel, mit Dateinamen.</param>
    /// <returns>Der Ordner; leer für eine Datei direkt in der Wurzel.</returns>
    public static string Of(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        int cut = relativePath.LastIndexOfAny(['/', '\\']);
        return cut < 0 ? string.Empty : relativePath[..cut].Replace('\\', '/');
    }
}
