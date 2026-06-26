namespace MdExplorer.Core;

/// <summary>
/// Zentrale, plattformabhängige Pfade der Anwendung.
/// </summary>
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

    /// <summary>Dateiname der Settings-Diff-Audit-Datei (JSON-Lines, ein Eintrag pro Save).</summary>
    public const string SettingsAuditLogFileName = "settings-audit.log";

    /// <summary>
    /// Wurzelverzeichnis aller Benutzerdaten: <c>%LOCALAPPDATA%\MdExplorer\</c>.
    /// Wird beim ersten Aufruf erzeugt, falls nicht vorhanden.
    /// </summary>
    public static string GetApplicationDataDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string applicationDataDirectory = Path.Combine(localAppData, ApplicationFolderName);
        _ = Directory.CreateDirectory(applicationDataDirectory);
        return applicationDataDirectory;
    }

    /// <summary>Vollständiger Pfad zur primären SQLite-Datenbank.</summary>
    public static string GetDatabasePath() =>
        Path.Combine(GetApplicationDataDirectory(), DatabaseFileName);

    /// <summary>
    /// Vollständiger Pfad zum Log-Verzeichnis. Wird beim ersten Aufruf erzeugt.
    /// </summary>
    public static string GetLogsDirectory()
    {
        string logsDirectory = Path.Combine(GetApplicationDataDirectory(), LogsFolderName);
        _ = Directory.CreateDirectory(logsDirectory);
        return logsDirectory;
    }

    /// <summary>Vollständiger Pfad zur Settings-Datei.</summary>
    public static string GetSettingsPath() =>
        Path.Combine(GetApplicationDataDirectory(), SettingsFileName);

    /// <summary>
    /// Verzeichnis für persistierte Settings-Snapshots. Wird beim ersten Aufruf erzeugt.
    /// Snapshot-Pattern: <c>settings.&lt;UTC-yyyyMMddTHHmmssFFF&gt;.json</c>.
    /// </summary>
    public static string GetSettingsHistoryDirectory()
    {
        string historyDirectory = Path.Combine(GetApplicationDataDirectory(), SettingsHistoryFolderName);
        _ = Directory.CreateDirectory(historyDirectory);
        return historyDirectory;
    }

    /// <summary>Vollständiger Pfad der Settings-Diff-Audit-Datei.</summary>
    public static string GetSettingsAuditLogPath() =>
        Path.Combine(GetApplicationDataDirectory(), SettingsAuditLogFileName);
}
