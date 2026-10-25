using System.IO;
using System.Linq;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Die von Hand pausierten Ordner aus <c>AppSettings.Indexing.UiExcludedFolders</c>.
/// </summary>
/// <remarks>
/// Verglichen wird über den vollständigen Pfad ohne abschließendes Trennzeichen und ohne
/// Rücksicht auf die Schreibweise: <c>C:\Notizen\</c> und <c>c:\notizen</c> sind derselbe
/// Ordner, und ein Eintrag soll weder doppelt stehen noch beim Wiederaufnehmen übersehen werden.
/// </remarks>
internal static class FolderExclusionList
{
    /// <summary>Bringt einen Pfad in die Form, in der verglichen wird; leer bleibt leer.</summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>Die Liste mit <paramref name="folder"/>; steht er schon darin, die unveränderte Liste.</summary>
    public static IReadOnlyList<string> With(IReadOnlyList<string> existing, string folder) =>
        Contains(existing, folder) ? existing : [.. existing, folder];

    /// <summary>Die Liste ohne <paramref name="folder"/>, in jeder Schreibweise.</summary>
    public static IReadOnlyList<string> Without(IReadOnlyList<string> existing, string folder) =>
        [.. existing.Where(item => !Matches(item, folder))];

    /// <summary>Ob <paramref name="absolutePath"/> selbst in der Liste steht; leere Einträge zählen nicht.</summary>
    public static bool Contains(IReadOnlyList<string> excluded, string absolutePath)
    {
        if (excluded.Count == 0 || string.IsNullOrWhiteSpace(absolutePath))
        {
            return false;
        }
        string normalized = Normalize(absolutePath);
        return excluded.Any(folder => !string.IsNullOrWhiteSpace(folder) && Matches(folder, normalized));
    }

    private static bool Matches(string entry, string normalizedFolder) =>
        string.Equals(Normalize(entry), normalizedFolder, StringComparison.OrdinalIgnoreCase);
}
