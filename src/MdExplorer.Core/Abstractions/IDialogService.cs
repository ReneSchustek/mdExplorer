namespace MdExplorer.Core.Abstractions;

/// <summary>
/// Plattform-Abstraktion für UI-Dialoge. Erlaubt ViewModels,
/// Ordner zu erfragen oder Hinweise anzuzeigen, ohne WPF zu kennen.
/// Tests stellen die Implementierung als Fake bereit.
/// </summary>
public interface IDialogService
{
    /// <summary>Zeigt einen Ordner-Auswahl-Dialog. Liefert <see langword="null"/> bei Abbruch.</summary>
    /// <param name="title">Anzeigetext im Dialog-Titel.</param>
    /// <param name="initialDirectory">Vorbelegtes Verzeichnis (optional).</param>
    string? PickDirectory(string title, string? initialDirectory);

    /// <summary>Zeigt eine Fehler-MessageBox mit OK-Button.</summary>
    void ShowError(string title, string message);

    /// <summary>
    /// Zeigt eine Bestätigungs-MessageBox. Liefert <see langword="true"/>, wenn der
    /// Benutzer die positive Aktion bestätigt, <see langword="false"/> bei Abbruch.
    /// </summary>
    bool Confirm(string title, string message);
}
