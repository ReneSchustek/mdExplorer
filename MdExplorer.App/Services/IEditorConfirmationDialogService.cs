namespace MdExplorer.App.Services;

/// <summary>
/// UI-Abstraktion fuer Bestaetigungsdialoge im Markdown-Editor. Wird vom
/// <see cref="MdExplorer.App.ViewModels.MarkdownEditorViewModel"/> aufgerufen, bevor
/// destruktive oder nicht-rueckgaengig machbare Operationen ausgefuehrt werden.
/// </summary>
internal interface IEditorConfirmationDialogService
{
    /// <summary>
    /// Fragt den Benutzer, ob die aktuellen Aenderungen wirklich gespeichert werden sollen.
    /// </summary>
    /// <returns><see langword="true"/>, wenn der Benutzer bestaetigt; <see langword="false"/> sonst.</returns>
    bool ConfirmSave();
}
