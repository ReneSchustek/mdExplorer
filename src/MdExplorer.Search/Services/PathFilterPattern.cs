using System.Text;

namespace MdExplorer.Search.Services;

/// <summary>
/// Macht aus den <c>path:</c>-Filtern einer Anfrage ein LIKE-Muster für die Spalte <c>Path</c>.
/// </summary>
/// <remarks>
/// <para>
/// Mehrere Filter gelten zugleich. Liegt der eine im anderen, bleibt der engere übrig; schließen
/// sie einander aus, kann keine Datei beide erfüllen, und es gibt kein Muster.
/// </para>
/// <para>
/// Gespeichert ist der relative Pfad des Betriebssystems, unter Windows mit Backslash. Wer
/// <c>path:notizen/2026</c> tippt, meint denselben Ordner; beide Trennzeichen werden deshalb auf
/// das des Systems gebracht. <c>%</c> und <c>_</c> sind im LIKE Platzhalter; ein Ordner
/// <c>my_docs</c> träfe sonst auch <c>myXdocs</c>. Sie werden mit <c>\</c> maskiert, und die
/// Abfrage nennt <c>\</c> als Escape-Zeichen.
/// </para>
/// </remarks>
internal static class PathFilterPattern
{
    /// <summary>Das Escape-Zeichen des Musters; die Abfrage muss es mit <c>ESCAPE</c> nennen.</summary>
    public const char EscapeCharacter = '\\';

    /// <summary>Baut das Muster.</summary>
    /// <param name="prefixes">Die Pfad-Anfänge der Anfrage, eventuell leer.</param>
    /// <param name="likePattern">Das Muster, oder <see langword="null"/>, wenn es keinen Filter gibt.</param>
    /// <returns><see langword="false"/>, wenn die Filter einander ausschließen.</returns>
    public static bool TryBuild(IReadOnlyList<string> prefixes, out string? likePattern)
    {
        ArgumentNullException.ThrowIfNull(prefixes);
        likePattern = null;
        if (prefixes.Count == 0)
        {
            return true;
        }

        List<string> normalized = [.. prefixes.Select(Normalize)];
        string narrowest = normalized.MaxBy(static prefix => prefix.Length)!; // mindestens ein Eintrag, siehe oben
        if (!normalized.TrueForAll(prefix => narrowest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        likePattern = Escape(narrowest) + "%";
        return true;
    }

    private static string Normalize(string prefix) =>
        prefix.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

    private static string Escape(string prefix)
    {
        StringBuilder builder = new(prefix.Length + 4);
        foreach (char character in prefix)
        {
            if (character is EscapeCharacter or '%' or '_')
            {
                _ = builder.Append(EscapeCharacter);
            }
            _ = builder.Append(character);
        }
        return builder.ToString();
    }
}
