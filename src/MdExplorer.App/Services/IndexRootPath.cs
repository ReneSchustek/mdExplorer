namespace MdExplorer.App.Services;

/// <summary>
/// Rechnet aus dem absoluten und dem relativen Pfad eines Index-Eintrags die Wurzel zurück, unter
/// der er steht.
/// </summary>
/// <remarks>
/// Der Eintrag trägt beide Pfade; die Einstellungen zu befragen, welche Wurzel gemeint war,
/// erübrigt sich damit. Verglichen wird nur an einer Ordnergrenze: Sonst gälte für eine Datei
/// im Ordner <c>C:\notes\xunter</c> mit einem relativen Pfad, der mit <c>unter/</c> beginnt,
/// die Wurzel <c>C:\notes\x</c>.
/// </remarks>
internal static class IndexRootPath
{
    /// <summary>Liefert die Wurzel des Eintrags.</summary>
    /// <param name="absolutePath">Absoluter Pfad der Datei.</param>
    /// <param name="relativePath">Pfad der Datei relativ zu ihrer Wurzel.</param>
    /// <returns>
    /// Die Wurzel mit <c>/</c> als Trenner und abschließendem <c>/</c>, oder
    /// <see langword="null"/>, wenn der relative Pfad nicht das Ende des absoluten ist.
    /// </returns>
    public static string? Of(string absolutePath, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(absolutePath);
        ArgumentNullException.ThrowIfNull(relativePath);

        string normalizedAbsolute = absolutePath.Replace('\\', '/');
        string normalizedRelative = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalizedRelative.Length == 0
            || !normalizedAbsolute.EndsWith("/" + normalizedRelative, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return normalizedAbsolute[..^normalizedRelative.Length];
    }
}
