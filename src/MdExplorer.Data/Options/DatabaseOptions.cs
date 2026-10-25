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

    /// <summary>Obergrenze des zulässigen Befehls-Timeouts in Sekunden (zehn Minuten).</summary>
    private const int MaxCommandTimeoutSeconds = 600;

    /// <summary>Vollständiger Pfad zur SQLite-Datenbank.</summary>
    [Required(AllowEmptyStrings = false)]
    public string DatabasePath { get; set; } = string.Empty;

    /// <summary>
    /// Befehls-Timeout in Sekunden. Standard: 30 s, derselbe Wert, den ADO.NET ohne Angabe nimmt.
    /// Er gilt auch für die Volltext-Befehle: EF Core legt ihn als <c>DefaultTimeout</c> an der
    /// Verbindung ab, und jeder dort angelegte Befehl übernimmt ihn.
    /// </summary>
    [Range(1, MaxCommandTimeoutSeconds)]
    public int CommandTimeoutSeconds { get; set; } = 30;
}
