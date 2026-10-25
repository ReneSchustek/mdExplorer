using System.Diagnostics.CodeAnalysis;
using System.Windows;
using MdExplorer.App.Localization;

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
            UiText.Get("Editor_SaveDialog_Question"),
            UiText.Get("Editor_SaveDialog_Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        return result == MessageBoxResult.Yes;
    }
}
