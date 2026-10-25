using MdExplorer.Core.Abstractions;

namespace MdExplorer.Indexer.Services;

/// <summary>
/// Bringt einen Wurzelpfad auf die Schreibweise, die er auf der Platte hat.
/// </summary>
/// <remarks>
/// <para>
/// Windows liefert beim Aufzählen den Wurzelpfad so zurück, wie er übergeben wurde. Steht in den
/// Einstellungen <c>C:\übersicht</c> und auf der Platte <c>C:\Übersicht</c>, tragen alle Pfade
/// darunter die Schreibweise aus den Einstellungen. Die Datenbank vergleicht Pfade mit der
/// NOCASE-Collation, und die faltet nur ASCII-Buchstaben: Für sie sind <c>ü</c> und <c>Ü</c>
/// verschieden, für Windows und den Indexer nicht. Ändert sich die Schreibweise in den
/// Einstellungen, stünde jede Datei zweimal im Index.
/// </para>
/// <para>
/// Eine eigene Collation in der Datenbank löste das auch, verlangte aber einen Neuaufbau der
/// Tabelle samt Triggern und machte die Datei für jedes Werkzeug ohne diese Collation unlesbar.
/// Mit der Schreibweise der Platte sind die Pfade dagegen gleiche Zeichenketten, und die Frage,
/// wie verglichen wird, stellt sich nicht.
/// </para>
/// </remarks>
internal static class RootPathCasing
{
    /// <summary>Liefert <paramref name="fullPath"/> in der Schreibweise der Platte.</summary>
    /// <param name="fileSystem">Dateisystem, über das die Ordnernamen gelesen werden.</param>
    /// <param name="fullPath">Ein vollständiger Ordnerpfad.</param>
    /// <returns>
    /// Der Pfad mit den Ordnernamen, wie sie auf der Platte stehen. Ein Name, den die Platte nicht
    /// kennt oder der sich nicht lesen lässt, bleibt wie übergeben.
    /// </returns>
    public static string Canonicalize(IFileSystem fileSystem, string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        string? driveRoot = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(driveRoot))
        {
            return fullPath;
        }

        string current = driveRoot;
        string[] segments = fullPath[driveRoot.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        foreach (string segment in segments)
        {
            current = Path.Combine(current, NameOnDisk(fileSystem, current, segment));
        }
        return current;
    }

    private static string NameOnDisk(IFileSystem fileSystem, string parent, string segment)
    {
        try
        {
            foreach (string child in fileSystem.EnumerateDirectories(parent))
            {
                string name = Path.GetFileName(child);
                if (string.Equals(name, segment, StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ein Elternordner ohne Leserecht: Die Schreibweise bleibt wie angegeben, die Wurzel
            // selbst ist trotzdem nutzbar. Ist sie es nicht, meldet das der Scan, der sie liest.
            return segment;
        }
        return segment;
    }
}
