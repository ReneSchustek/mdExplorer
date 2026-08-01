namespace MdExplorer.App.ViewModels;

/// <summary>
/// Argumente des <see cref="MarkdownEditorViewModel.Saved"/>-Events. Enthält den
/// Text, der gerade auf den Datenträger geschrieben wurde, damit Konsumenten
/// (z. B. <see cref="DocumentPanelViewModel"/>) die Preview frisch rendern können.
/// </summary>
internal sealed class EditorSavedEventArgs(string savedText) : EventArgs
{
    /// <summary>Der gespeicherte Text in der Form, die auf die Datei geschrieben wurde.</summary>
    public string SavedText { get; } = savedText ?? throw new ArgumentNullException(nameof(savedText));
}
