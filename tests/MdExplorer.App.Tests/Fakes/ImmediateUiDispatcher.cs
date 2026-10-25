using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>Führt sofort auf dem aufrufenden Thread aus und zählt die Aufrufe.</summary>
/// <remarks>
/// Für Tests, in denen die Reihenfolge zwischen Hintergrund und Oberfläche keine Rolle spielt.
/// Wer prüfen will, dass sich eine Meldung hinter die laufende Arbeit stellt, nimmt
/// <c>QueuedUiDispatcher</c>.
/// </remarks>
internal sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public int InvokeCount { get; private set; }

    public int PostCount { get; private set; }

    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        InvokeCount++;
        action();
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        PostCount++;
        action();
    }
}
