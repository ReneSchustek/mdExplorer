using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>Rückfrage „wirklich speichern?“ mit vorgegebener Antwort, die ihre Aufrufe zählt.</summary>
internal sealed class FakeEditorConfirmation : IEditorConfirmationDialogService
{
    /// <summary>Die Antwort auf jede Rückfrage; ab Werk wird bestätigt.</summary>
    public bool Result { get; set; } = true;

    /// <summary>Wie oft gefragt wurde.</summary>
    public int CallCount { get; private set; }

    public bool ConfirmSave()
    {
        CallCount++;
        return Result;
    }
}
