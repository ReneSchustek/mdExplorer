using System.IO;
using MdExplorer.App.Services;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Schützt die Änderungen im Editor vor dem stillen Überschreiben.
/// </summary>
/// <remarks>
/// Laden ersetzt den Text im Editor. Ohne diese Frage war getippte Arbeit verloren, sobald der
/// Nutzer eine andere Datei anklickte oder das Programm schloss.
/// </remarks>
internal sealed class UnsavedChangesGuard : IUnsavedChangesGuard
{
    private readonly MarkdownEditorViewModel _editor;
    private readonly IUnsavedChangesDialogService _dialog;

    /// <summary>Erzeugt den Wächter für den Editor der Anwendung.</summary>
    /// <param name="editor">Der Editor, dessen Änderungen geschützt werden.</param>
    /// <param name="dialog">Stellt die Frage an den Nutzer.</param>
    public UnsavedChangesGuard(MarkdownEditorViewModel editor, IUnsavedChangesDialogService dialog)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(dialog);
        _editor = editor;
        _dialog = dialog;
    }

    /// <inheritdoc />
    public bool HasUnsavedChanges => _editor.IsDirty;

    /// <inheritdoc />
    public async Task<bool> ConfirmLeaveAsync(CancellationToken cancellationToken)
    {
        if (!_editor.IsDirty)
        {
            return true;
        }

        UnsavedChangesDecision decision = _dialog.Ask(Path.GetFileName(_editor.FilePath ?? string.Empty));
        switch (decision)
        {
            case UnsavedChangesDecision.Save:
                // Scheitert das Speichern, nennt der Editor den Grund in seiner Statuszeile. Der
                // Wechsel unterbleibt, damit die Änderungen nicht doch noch verloren gehen.
                return await _editor.SaveWithoutConfirmationAsync(cancellationToken).ConfigureAwait(true);
            case UnsavedChangesDecision.Discard:
                _editor.DiscardChanges();
                return true;
            default:
                return false;
        }
    }
}
