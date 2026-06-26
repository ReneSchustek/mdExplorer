using System.ComponentModel.DataAnnotations;

namespace MdExplorer.Data.Options;

/// <summary>
/// Konfiguration für die SQLite-Datenbank der Anwendung.
/// Wird beim Start via <c>ValidateDataAnnotations</c> + <c>ValidateOnStart</c> geprüft.
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>Konfigurations-Sektion in <c>IConfiguration</c>.</summary>
    public const string SectionName = "Database";

    /// <summary>Vollständiger Pfad zur SQLite-Datenbank.</summary>
    [Required(AllowEmptyStrings = false)]
    public string DatabasePath { get; set; } = string.Empty;

    /// <summary>
    /// Befehls-Timeout in Sekunden für lange laufende Operationen
    /// (z. B. FTS5-Bulk-Updates). Standard: 30 s.
    /// </summary>
    [Range(1, 600)]
    public int CommandTimeoutSeconds { get; set; } = 30;
}
