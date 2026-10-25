using MdExplorer.App.Localization;

namespace MdExplorer.App.Services;

/// <summary>
/// Texte zu Dateien, die das Programm von außen öffnen soll: per Doppelklick, über „Öffnen mit“
/// oder aus einer zweiten Instanz.
/// </summary>
internal static class OpenFileMessages
{
    /// <summary>Titel der Meldung zu einem abgewiesenen Pfad.</summary>
    public static string RejectedTitle => UiText.Get("OpenFile_Rejected_Title");

    /// <summary>Titel der Meldung, wenn die laufende Instanz nicht antwortet.</summary>
    public static string HandOverFailedTitle => UiText.Get("OpenFile_HandOverFailed_Title");

    /// <summary>Meldung zu einem abgewiesenen Pfad.</summary>
    /// <param name="check">Das Ergebnis der Prüfung; es muss eine Abweisung sein.</param>
    public static string Rejected(MarkdownPathCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (check.Problem is not { } problem)
        {
            throw new ArgumentException("Die Prüfung hat nichts abgewiesen.", nameof(check));
        }

        string value = check.RejectedValue ?? string.Empty;
        return problem switch
        {
            MarkdownPathProblem.NotFound => UiText.Format("OpenFile_Rejected_NotFound", value),
            MarkdownPathProblem.UnsupportedExtension => UiText.Format("OpenFile_Rejected_UnsupportedExtension", value),
            MarkdownPathProblem.InvalidPath => UiText.Format("OpenFile_Rejected_InvalidPath", value),
            _ => throw new ArgumentOutOfRangeException(nameof(check), problem, "Unbekannter Grund."),
        };
    }

    /// <summary>Meldung, wenn die laufende Instanz nicht rechtzeitig geantwortet hat.</summary>
    /// <param name="absolutePath">Die Datei, die übergeben werden sollte; <see langword="null"/>, wenn keine.</param>
    public static string HandOverFailed(string? absolutePath) => absolutePath is null
        ? UiText.Get("OpenFile_HandOverFailed_NoFile")
        : UiText.Format("OpenFile_HandOverFailed_File", absolutePath);
}
