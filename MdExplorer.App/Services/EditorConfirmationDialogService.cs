using System.Windows;

namespace MdExplorer.App.Services;

/// <summary>
/// Standard-Implementierung von <see cref="IEditorConfirmationDialogService"/> ueber
/// <see cref="MessageBox"/>. Strikt nur in der WPF-Schicht aktiv.
/// </summary>
internal sealed class EditorConfirmationDialogService : IEditorConfirmationDialogService
{
    /// <inheritdoc />
    public bool ConfirmSave()
    {
        MessageBoxResult result = MessageBox.Show(
            "Moechten Sie die Aenderungen wirklich speichern?",
            "Aenderungen speichern",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        return result == MessageBoxResult.Yes;
    }
}
