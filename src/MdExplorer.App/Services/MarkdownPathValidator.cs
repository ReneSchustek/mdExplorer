using System.IO;
using MdExplorer.Core.Abstractions;

namespace MdExplorer.App.Services;

/// <summary>
/// Prüft einen Dateipfad, den das Programm von außen bekommt: aus der Befehlszeile beim Start
/// oder von einer zweiten Instanz, die ihn weiterreicht.
/// </summary>
/// <remarks>
/// Erst die Form, dann die Endung, zuletzt die Platte: Ein Pfad mit falscher Endung wird abgewiesen,
/// ohne dass dafür auf den Datenträger zugegriffen wird.
/// </remarks>
internal sealed class MarkdownPathValidator
{
    private static readonly string[] SupportedExtensions = [".md", ".markdown"];

    private readonly IFileSystem _fileSystem;

    /// <summary>Erzeugt die Prüfung.</summary>
    /// <param name="fileSystem">Wo nachgesehen wird, ob die Datei existiert.</param>
    public MarkdownPathValidator(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <summary>Prüft das erste Argument der Befehlszeile; weitere werden nicht ausgewertet.</summary>
    /// <param name="arguments">Die Argumente, wie Windows sie übergibt.</param>
    /// <param name="currentDirectory">Bezug für einen relativen Pfad.</param>
    /// <returns>Das Ergebnis; <see cref="MarkdownPathCheck.None"/>, wenn kein Argument da ist.</returns>
    public MarkdownPathCheck CheckCommandLine(IReadOnlyList<string> arguments, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);

        return arguments.Count == 0 ? MarkdownPathCheck.None : Check(arguments[0], currentDirectory);
    }

    /// <summary>
    /// Prüft einen Pfad, den eine zweite Instanz weitergereicht hat. Er muss vollständig sein:
    /// Das Arbeitsverzeichnis der anderen Instanz ist hier unbekannt.
    /// </summary>
    /// <param name="absolutePath">Der Pfad; <see langword="null"/> heißt, nur das Fenster soll nach vorn.</param>
    /// <returns>Das Ergebnis.</returns>
    public MarkdownPathCheck CheckForwarded(string? absolutePath) =>
        absolutePath is null ? MarkdownPathCheck.None : Check(absolutePath, baseDirectory: null);

    private static string Unquote(string rawValue)
    {
        // Eine Verknüpfung ohne Anführungszeichen um %1 oder ein Aufruf aus einem Skript liefert
        // den Pfad manchmal samt Anführungszeichen; im Dateinamen sind sie unter Windows ohnehin
        // nicht erlaubt.
        return rawValue.Trim().Trim('"').Trim();
    }

    private static string? Normalize(string value, string? baseDirectory)
    {
        try
        {
            if (Path.IsPathFullyQualified(value))
            {
                return Path.GetFullPath(value);
            }
            return baseDirectory is null ? null : Path.GetFullPath(value, baseDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool HasSupportedExtension(string path)
    {
        string extension = Path.GetExtension(path);
        return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private MarkdownPathCheck Check(string rawValue, string? baseDirectory)
    {
        string value = Unquote(rawValue);
        if (value.Length == 0)
        {
            return MarkdownPathCheck.Rejected(rawValue, MarkdownPathProblem.InvalidPath);
        }

        string? fullPath = Normalize(value, baseDirectory);
        if (fullPath is null)
        {
            return MarkdownPathCheck.Rejected(value, MarkdownPathProblem.InvalidPath);
        }
        if (!HasSupportedExtension(fullPath))
        {
            return MarkdownPathCheck.Rejected(fullPath, MarkdownPathProblem.UnsupportedExtension);
        }
        if (!_fileSystem.FileExists(fullPath))
        {
            return MarkdownPathCheck.Rejected(fullPath, MarkdownPathProblem.NotFound);
        }
        return MarkdownPathCheck.Accepted(fullPath);
    }
}

/// <summary>Warum ein Pfad von außen nicht geöffnet wird.</summary>
internal enum MarkdownPathProblem
{
    /// <summary>Leer, mit unzulässigen Zeichen oder relativ, wo ein vollständiger Pfad verlangt ist.</summary>
    InvalidPath,

    /// <summary>Weder <c>.md</c> noch <c>.markdown</c>.</summary>
    UnsupportedExtension,

    /// <summary>Die Datei gibt es nicht.</summary>
    NotFound,
}

/// <summary>Ergebnis der Prüfung eines Pfads von außen.</summary>
/// <param name="AbsolutePath">Der normalisierte Pfad, wenn er geöffnet werden darf.</param>
/// <param name="RejectedValue">Was abgewiesen wurde, so weit es sich lesen ließ.</param>
/// <param name="Problem">Der Grund der Abweisung.</param>
internal sealed record MarkdownPathCheck(string? AbsolutePath, string? RejectedValue, MarkdownPathProblem? Problem)
{
    /// <summary>Kein Pfad übergeben; es gibt nichts zu öffnen und nichts zu melden.</summary>
    public static MarkdownPathCheck None { get; } = new(AbsolutePath: null, RejectedValue: null, Problem: null);

    /// <summary>Wahr, wenn weder ein Pfad noch eine Abweisung vorliegt.</summary>
    public bool IsNone => AbsolutePath is null && Problem is null;

    /// <summary>Ein Pfad, der geöffnet werden darf.</summary>
    /// <param name="absolutePath">Der normalisierte Pfad.</param>
    public static MarkdownPathCheck Accepted(string absolutePath) => new(absolutePath, RejectedValue: null, Problem: null);

    /// <summary>Ein abgewiesener Pfad.</summary>
    /// <param name="value">Was abgewiesen wurde.</param>
    /// <param name="problem">Warum.</param>
    public static MarkdownPathCheck Rejected(string value, MarkdownPathProblem problem) => new(AbsolutePath: null, value, problem);
}
