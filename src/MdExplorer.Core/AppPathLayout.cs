using MdExplorer.Core.Abstractions;

namespace MdExplorer.Core;

/// <summary>
/// Wo unter einem Anwendungsverzeichnis Datenbank, Protokolle, Einstellungen und Browserdaten
/// liegen.
/// </summary>
/// <remarks>
/// Die Klasse rechnet nur Pfade aus und legt nichts an. Angelegt wird einmal beim Start über
/// <see cref="EnsureDirectories"/>; ein Getter, der nebenbei Ordner erzeugt, legte sie auch in
/// jedem Test an, der nur einen Pfad wissen wollte.
/// </remarks>
/// <param name="ApplicationDataDirectory">Wurzel aller Benutzerdaten der Anwendung.</param>
public sealed record AppPathLayout(string ApplicationDataDirectory)
{
    /// <summary>Vollständiger Pfad zur primären SQLite-Datenbank.</summary>
    public string DatabasePath => Path.Combine(ApplicationDataDirectory, AppPaths.DatabaseFileName);

    /// <summary>Verzeichnis der täglichen Log-Dateien.</summary>
    public string LogsDirectory => Path.Combine(ApplicationDataDirectory, AppPaths.LogsFolderName);

    /// <summary>Vollständiger Pfad zur Settings-Datei.</summary>
    public string SettingsPath => Path.Combine(ApplicationDataDirectory, AppPaths.SettingsFileName);

    /// <summary>
    /// Verzeichnis für Settings-Snapshots. Muster: <c>settings.&lt;UTC-yyyyMMddTHHmmssFFF&gt;.json</c>.
    /// </summary>
    public string SettingsHistoryDirectory => Path.Combine(ApplicationDataDirectory, AppPaths.SettingsHistoryFolderName);

    /// <summary>Vollständiger Pfad der Settings-Diff-Audit-Datei.</summary>
    public string SettingsAuditLogPath => Path.Combine(ApplicationDataDirectory, AppPaths.SettingsAuditLogFileName);

    /// <summary>Verzeichnis für die Benutzerdaten der eingebetteten Browser-Ansichten.</summary>
    /// <remarks>
    /// Ohne ausdrückliche Angabe schreibt die Browser-Komponente neben die Programmdatei, nach
    /// einer Installation also dorthin, wo ein Benutzer keine Rechte hat.
    /// </remarks>
    public string WebView2DataDirectory => Path.Combine(ApplicationDataDirectory, AppPaths.WebView2FolderName);

    /// <summary>
    /// Legt Wurzel, Protokoll-, Verlaufs- und Browserdaten-Verzeichnis an. Vorhandene Ordner sind
    /// kein Fehler.
    /// </summary>
    /// <param name="fileSystem">Dateisystem, über das angelegt wird.</param>
    public void EnsureDirectories(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        fileSystem.EnsureDirectoryExists(ApplicationDataDirectory);
        fileSystem.EnsureDirectoryExists(LogsDirectory);
        fileSystem.EnsureDirectoryExists(SettingsHistoryDirectory);
        fileSystem.EnsureDirectoryExists(WebView2DataDirectory);
    }
}
