using System.Diagnostics.CodeAnalysis;
using System.Windows;
using MdExplorer.App.Localization;

namespace MdExplorer.App.Services;

/// <summary>
/// Stellt die Frage nach ungespeicherten Änderungen als Meldungsfenster mit Ja, Nein und
/// Abbrechen.
/// </summary>
/// <remarks>
/// Abbrechen ist die Vorgabe für Escape und das Schließen der Meldung: Wer die Frage wegklickt,
/// soll beim Dokument bleiben, nicht seine Änderungen verlieren.
/// </remarks>
[ExcludeFromCodeCoverage]
internal sealed class UnsavedChangesDialogService : IUnsavedChangesDialogService
{
    /// <inheritdoc />
    public UnsavedChangesDecision Ask(string documentName)
    {
        MessageBoxResult result = MessageBox.Show(
            UiText.Format("Editor_UnsavedDialog_Question", documentName),
            UiText.Get("Editor_UnsavedDialog_Title"),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        return result switch
        {
            MessageBoxResult.Yes => UnsavedChangesDecision.Save,
            MessageBoxResult.No => UnsavedChangesDecision.Discard,
            _ => UnsavedChangesDecision.Cancel,
        };
    }
}
