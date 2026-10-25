namespace MdExplorer.App.Services;

/// <summary>
/// Fragt, was mit ungespeicherten Änderungen geschehen soll, bevor das Dokument den Editor
/// verlässt.
/// </summary>
internal interface IUnsavedChangesDialogService
{
    /// <summary>Stellt die Frage für ein Dokument.</summary>
    /// <param name="documentName">Name der Datei, wie der Nutzer sie kennt.</param>
    /// <returns>Speichern, Verwerfen oder Abbrechen.</returns>
    UnsavedChangesDecision Ask(string documentName);
}
