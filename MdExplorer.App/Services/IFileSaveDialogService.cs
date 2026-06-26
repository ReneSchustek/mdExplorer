namespace MdExplorer.App.Services;

/// <summary>
/// Abstraktion für das Öffnen eines „Speichern unter…"-Dialogs. Trennt
/// die ViewModel-Schicht vom <see cref="Microsoft.Win32.SaveFileDialog"/>.
/// </summary>
internal interface IFileSaveDialogService
{
    /// <summary>
    /// Öffnet den Speichern-Dialog mit dem übergebenen Default-Dateinamen und Filter.
    /// </summary>
    /// <param name="defaultFileName">Vorbelegter Dateiname inkl. Endung.</param>
    /// <param name="filter">WPF-Standard-Filter-String, z. B. <c>"Text|*.txt"</c>.</param>
    /// <returns>
    /// Vollständiger Pfad bei Bestätigung; <see langword="null"/> bei Abbruch.
    /// </returns>
    string? PromptForSavePath(string defaultFileName, string filter);
}
