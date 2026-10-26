using System.IO;
using MdExplorer.App.Services;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Schützt die Änderungen im Editor vor dem stillen Überschreiben.
/// </summary>
/// <remarks>
/// <para>
/// Laden ersetzt den Text im Editor. Ohne diese Frage wäre getippte Arbeit verloren, sobald der
/// Nutzer eine andere Datei anklickt oder das Programm schließt.
/// </para>
/// <para>
/// Es läuft immer nur eine Rückfrage. Der Dialog ist modal, verarbeitet aber weiter Nachrichten;
/// ein Doppelklick im Explorer erreicht das Programm also auch, während er offen steht. Eine
/// zweite Rückfrage darüber hätte der ersten die Entscheidung abgenommen: innen „Verwerfen“,
/// außen „Speichern“, und der Text wäre trotzdem weg. Wer fragt, während schon gefragt wird,
/// bekommt deshalb „bleiben“.
/// </para>
/// </remarks>
internal sealed class UnsavedChangesGuard : IUnsavedChangesGuard
{
    private readonly MarkdownEditorViewModel _editor;
    private readonly IUnsavedChangesDialogService _dialog;
    private bool _isConfirming;

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
        if (_isConfirming)
        {
            return false;
        }

        _isConfirming = true;
        try
        {
            return await AskAndApplyAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _isConfirming = false;
        }
    }

    private async Task<bool> AskAndApplyAsync(CancellationToken cancellationToken)
    {
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
