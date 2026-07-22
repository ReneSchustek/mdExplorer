using MdExplorer.Core.Abstractions;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>Test-Fake für <see cref="IDialogService"/> — protokolliert Aufrufe, liefert konfigurierte Antworten.</summary>
internal sealed class FakeDialogService : IDialogService
{
    /// <summary>Wert, den <see cref="PickDirectory"/> zurückliefert (<see langword="null"/> = Abbruch).</summary>
    public string? DirectoryToReturn { get; set; }

    /// <summary>Anzahl der <see cref="PickDirectory"/>-Aufrufe.</summary>
    public int PickDirectoryCalls { get; private set; }

    /// <summary>Titel/Nachricht der letzten <see cref="ShowError"/>-Anzeige, oder <see langword="null"/>.</summary>
    public (string Title, string Message)? LastError { get; private set; }

    /// <summary>Antwort für <see cref="Confirm"/>.</summary>
    public bool ConfirmResult { get; set; }

    /// <inheritdoc />
    public string? PickDirectory(string title, string? initialDirectory)
    {
        PickDirectoryCalls++;
        return DirectoryToReturn;
    }

    /// <inheritdoc />
    public void ShowError(string title, string message) => LastError = (title, message);

    /// <inheritdoc />
    public bool Confirm(string title, string message) => ConfirmResult;
}
