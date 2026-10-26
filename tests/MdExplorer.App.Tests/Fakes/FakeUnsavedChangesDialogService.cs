using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>
/// Beantwortet die Frage nach ungespeicherten Änderungen mit einer festen Antwort und merkt sich,
/// ob und für welche Datei gefragt wurde.
/// </summary>
internal sealed class FakeUnsavedChangesDialogService : IUnsavedChangesDialogService
{
    public UnsavedChangesDecision Decision { get; set; } = UnsavedChangesDecision.Cancel;

    public int AskCount { get; private set; }

    public string? LastDocumentName { get; private set; }

    /// <summary>Läuft, während der Dialog offen steht, wie ein Auftrag, den der Dialog nebenbei verarbeitet.</summary>
    public Action? WhileOpen { get; set; }

    public UnsavedChangesDecision Ask(string documentName)
    {
        AskCount++;
        LastDocumentName = documentName;
        WhileOpen?.Invoke();
        return Decision;
    }
}
