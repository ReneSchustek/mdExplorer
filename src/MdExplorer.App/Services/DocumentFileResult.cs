namespace MdExplorer.App.Services;

/// <summary>Ausgang eines Vorgangs an Datei und Index.</summary>
/// <param name="Succeeded">Ob der Vorgang vollständig durchlief.</param>
/// <param name="Message">Was passiert ist, für die Statuszeile, in ganzen Sätzen.</param>
/// <param name="NewAbsolutePath">Der neue Pfad, sofern die Datei noch existiert.</param>
internal sealed record DocumentFileResult(bool Succeeded, string Message, string? NewAbsolutePath)
{
    /// <summary>Erzeugt ein Fehlschlag-Ergebnis mit Begründung.</summary>
    public static DocumentFileResult Failed(string message) => new(false, message, null);
}
