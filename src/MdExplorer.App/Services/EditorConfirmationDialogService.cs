using System.Windows;

using System.Diagnostics.CodeAnalysis;

namespace MdExplorer.App.Services;

/// <summary>
/// Standard-Implementierung von <see cref="IEditorConfirmationDialogService"/> über
/// <see cref="MessageBox"/>. Strikt nur in der WPF-Schicht aktiv.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class EditorConfirmationDialogService : IEditorConfirmationDialogService
{
    /// <inheritdoc />
    public bool ConfirmSave()
    {
        MessageBoxResult result = MessageBox.Show(
            "Möchten Sie die Änderungen wirklich speichern?",
            "Änderungen speichern",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        return result == MessageBoxResult.Yes;
    }
}
