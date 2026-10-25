using MdExplorer.App.ViewModels;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>
/// Wächter ohne Editor: lässt den Wechsel zu oder hält ihn an, je nach Vorgabe. Für Tests, die
/// den Wechsel selbst prüfen und nicht die Frage danach.
/// </summary>
internal sealed class StubUnsavedChangesGuard : IUnsavedChangesGuard
{
    public bool AllowLeave { get; set; } = true;

    public bool HasUnsavedChanges { get; set; }

    public int ConfirmCount { get; private set; }

    public Task<bool> ConfirmLeaveAsync(CancellationToken cancellationToken)
    {
        ConfirmCount++;
        return Task.FromResult(AllowLeave);
    }
}
