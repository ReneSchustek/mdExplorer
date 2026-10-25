namespace MdExplorer.Indexer.Services;

/// <summary>
/// Prüft, ob ein Pfad in einem Ordner liegt. Verglichen wird ohne Rücksicht auf Groß- und
/// Kleinschreibung und immer bis zu einem Trenner: <c>C:\Notes-alt</c> liegt nicht in <c>C:\Notes</c>.
/// </summary>
internal static class PathScope
{
    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>Kürzt abschließende Trenner, damit Ordner mit und ohne Trenner gleich verglichen werden.</summary>
    public static string TrimSeparators(string path) => path.TrimEnd(Separators);

    /// <summary>Wahr, wenn <paramref name="path"/> echt unterhalb von <paramref name="directory"/> liegt.</summary>
    public static bool IsBelow(string path, string directory)
    {
        string trimmed = TrimSeparators(directory);
        return path.Length > trimmed.Length
            && path.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)
            && Separators.Contains(path[trimmed.Length]);
    }

    /// <summary>Wahr, wenn <paramref name="path"/> der Ordner selbst ist oder darunter liegt.</summary>
    public static bool IsAtOrBelow(string path, string directory) =>
        string.Equals(TrimSeparators(path), TrimSeparators(directory), StringComparison.OrdinalIgnoreCase)
        || IsBelow(path, directory);
}
