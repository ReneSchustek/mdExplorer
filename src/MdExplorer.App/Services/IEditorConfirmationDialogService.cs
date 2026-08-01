namespace MdExplorer.App.Services;

/// <summary>
/// UI-Abstraktion für Bestätigungsdialoge im Markdown-Editor. Wird vom
/// <see cref="MdExplorer.App.ViewModels.MarkdownEditorViewModel"/> aufgerufen, bevor
/// destruktive oder nicht-rückgängig machbare Operationen ausgeführt werden.
/// </summary>
internal interface IEditorConfirmationDialogService
{
    /// <summary>
    /// Fragt den Benutzer, ob die aktuellen Änderungen wirklich gespeichert werden sollen.
    /// </summary>
    /// <returns><see langword="true"/>, wenn der Benutzer bestätigt; <see langword="false"/> sonst.</returns>
    bool ConfirmSave();
}
