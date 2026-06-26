using System.Collections.ObjectModel;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Text;

namespace MdExplorer.Core.Settings;

/// <summary>
/// Liest eine <c>.mdignore</c>-Datei nach .gitignore-Konvention:
/// Kommentare (<c>#</c>) und Leerzeilen werden übersprungen,
/// jede sonstige Zeile ist ein Glob-Muster (mit optionaler <c>!</c>-Negation).
/// </summary>
public sealed class MdIgnoreReader
{
    private const string IgnoreFileName = ".mdignore";

    private readonly IFileSystem _fileSystem;

    /// <summary>Erzeugt den Reader und injiziert das Dateisystem.</summary>
    public MdIgnoreReader(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <summary>Dateiname der Ignore-Datei pro Verzeichnis.</summary>
    public static string FileName => IgnoreFileName;

    /// <summary>
    /// Versucht, im angegebenen Verzeichnis eine <c>.mdignore</c> zu lesen.
    /// Liefert eine leere Liste, wenn die Datei nicht existiert; ungültige
    /// Zeilen werden übersprungen.
    /// </summary>
    public ReadOnlyCollection<string> Read(string directoryAbsolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryAbsolutePath);

        string ignoreFilePath = Path.Combine(directoryAbsolutePath, IgnoreFileName);
        if (!_fileSystem.FileExists(ignoreFilePath))
        {
            return ReadOnlyCollection<string>.Empty;
        }

        byte[] bytes = _fileSystem.ReadAllBytes(ignoreFilePath);
        string content = Utf8Decoder.DecodeNoBom(bytes);
        return ParseLines(content).AsReadOnly();
    }

    private static List<string> ParseLines(string content)
    {
        List<string> patterns = [];
        foreach (string rawLine in content.Split('\n'))
        {
            string trimmed = rawLine.Trim('\r', ' ', '\t');
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }
            patterns.Add(trimmed);
        }
        return patterns;
    }
}
