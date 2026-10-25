using System.IO;

namespace MdExplorer.App.Services;

/// <summary>
/// Woran sich die relativen Bildpfade eines Dokuments in der Vorschau halten: der Ordner, den
/// der virtuelle Rechnername freigibt, und der Ordner des Dokuments darin.
/// </summary>
/// <param name="Folder">Der freigegebene Ordner; für einen Index-Eintrag seine Wurzel.</param>
/// <param name="RelativeDirectory">Ordner des Dokuments relativ zu <paramref name="Folder"/>, mit <c>/</c> getrennt; leer, wenn es direkt darin liegt.</param>
/// <remarks>
/// Freigegeben wird die Wurzel und nicht der Ordner des Dokuments, damit <c>../bilder/x.png</c>
/// aus einem Unterordner sein Ziel findet. Aufgelöst wird hier und nicht im Browser: Der kappte
/// überzählige <c>..</c> stillschweigend an der Wurzel und zeigte eine gleichnamige, aber falsche
/// Datei. Ein Pfad über die Wurzel hinaus bekommt hier keine Adresse und lädt nicht.
/// </remarks>
internal sealed record PreviewImageBase(string Folder, string RelativeDirectory)
{
    /// <summary>Bestimmt die Basis einer Datei.</summary>
    /// <param name="absolutePath">Absoluter Pfad des Dokuments.</param>
    /// <param name="relativePath">
    /// Sein Pfad relativ zur Index-Wurzel, oder <see langword="null"/> für eine Datei, die der Index
    /// nicht kennt. Dann wird nur ihr eigener Ordner freigegeben.
    /// </param>
    /// <returns>Die Basis für die Bildpfade des Dokuments.</returns>
    public static PreviewImageBase ForFile(string absolutePath, string? relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        if (relativePath is not null && IndexRootPath.Of(absolutePath, relativePath) is { } root)
        {
            string relative = relativePath.Replace('\\', '/').TrimStart('/');
            int lastSlash = relative.LastIndexOf('/');
            return new PreviewImageBase(
                Path.TrimEndingDirectorySeparator(root.Replace('/', Path.DirectorySeparatorChar)),
                lastSlash < 0 ? string.Empty : relative[..lastSlash]);
        }

        return new PreviewImageBase(Path.GetDirectoryName(absolutePath) ?? string.Empty, string.Empty);
    }

    /// <summary>Löst einen relativen Bildpfad zu einer Adresse unter dem virtuellen Rechnernamen auf.</summary>
    /// <param name="source">Der Pfad, wie er im Dokument steht; ein führendes <c>/</c> meint die Wurzel.</param>
    /// <returns>Die Adresse, oder <see langword="null"/>, wenn der Pfad über die Wurzel hinausführt.</returns>
    public string? Resolve(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Anfrage und Sprungmarke gehören nicht zum Pfad, bleiben aber an der Adresse.
        int cut = source.IndexOfAny(['?', '#']);
        string path = (cut < 0 ? source : source[..cut]).Replace('\\', '/');
        string suffix = cut < 0 ? string.Empty : source[cut..];

        List<string> segments = path.StartsWith('/')
            ? []
            : [.. RelativeDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries)];
        foreach (string segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            // Der Browser liest auch „%2e%2e“ als „..“; geprüft wird deshalb die entschlüsselte Form.
            string decoded = Uri.UnescapeDataString(segment);
            if (decoded == ".")
            {
                continue;
            }
            if (decoded == "..")
            {
                if (segments.Count == 0)
                {
                    return null;
                }
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }

        return "https://" + PreviewHtmlBuilder.ImageHost + "/" + string.Join('/', segments) + suffix;
    }
}
