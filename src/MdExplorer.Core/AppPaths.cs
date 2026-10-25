namespace MdExplorer.Core;

/// <summary>
/// Zentrale Pfade der Anwendung unter <c>%LOCALAPPDATA%\MdExplorer\</c>.
/// </summary>
/// <remarks>
/// <see cref="Default"/> rechnet nur und legt nichts an; das tut einmal der Start
/// (<see cref="AppPathLayout.EnsureDirectories"/> im <c>AppInitializer</c>). Statisch bleibt der
/// Zugang, weil Protokoll und Datenbankpfad schon gebraucht werden, bevor die Abhängigkeits-
/// verwaltung steht. Wer testen will, nimmt <see cref="AppPathLayout"/> mit eigener Wurzel.
/// </remarks>
public static class AppPaths
{
    /// <summary>Name des Anwendungsverzeichnisses unter <c>%LOCALAPPDATA%</c>.</summary>
    public const string ApplicationFolderName = "MdExplorer";

    /// <summary>Dateiname der primären SQLite-Datenbank.</summary>
    public const string DatabaseFileName = "app.db";

    /// <summary>Unterverzeichnis für tägliche Log-Dateien.</summary>
    public const string LogsFolderName = "logs";

    /// <summary>Dateiname der Settings-Datei.</summary>
    public const string SettingsFileName = "settings.json";

    /// <summary>Unterverzeichnis für Settings-Snapshots des Audit-Trails.</summary>
    public const string SettingsHistoryFolderName = "settings-history";

    /// <summary>Unterverzeichnis für die Benutzerdaten der eingebetteten Browser-Ansichten.</summary>
    public const string WebView2FolderName = "webview2";

    /// <summary>Dateiname der Settings-Diff-Audit-Datei (JSON-Lines, ein Eintrag pro Save).</summary>
    public const string SettingsAuditLogFileName = "settings-audit.log";

    /// <summary>Die Pfade unter dem Anwendungsverzeichnis dieses Benutzers.</summary>
    public static AppPathLayout Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ApplicationFolderName));
}
