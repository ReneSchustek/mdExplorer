using System.ComponentModel.DataAnnotations;

namespace MdExplorer.Update.Options;

/// <summary>
/// Konfiguration des Update-Moduls. Wird beim Hosten via <c>ValidateDataAnnotations</c> +
/// <c>ValidateOnStart</c> geprüft. Die Repository-Koordinaten zeigen standardmäßig auf das
/// öffentliche GitHub-Repository der Anwendung; Tests und Sonderfälle können sie überschreiben.
/// </summary>
public sealed class UpdateOptions
{
    /// <summary>Konfigurations-Sektion in <c>IConfiguration</c>.</summary>
    public const string SectionName = "Update";

    /// <summary>Obergrenze des Prüfintervalls: eine Woche (24 Stunden × 7 Tage).</summary>
    private const int MaxCheckIntervalHours = 24 * 7;

    /// <summary>Obergrenze des HTTP-Timeouts in Sekunden.</summary>
    private const int MaxRequestTimeoutSeconds = 60;

    /// <summary>GitHub-Eigentümer (Benutzer/Organisation) des Repositories.</summary>
    [Required]
    public string RepositoryOwner { get; set; } = "ReneSchustek";

    /// <summary>Name des GitHub-Repositories.</summary>
    [Required]
    public string RepositoryName { get; set; } = "mdExplorer";

    /// <summary>
    /// Mindestabstand zwischen zwei tatsächlichen Netzprüfungen in Stunden. Ohne Anmeldung
    /// erlaubt die GitHub-API 60 Anfragen je Stunde und Adresse; mit häufigen Programmstarts
    /// käme man dem nahe. Default: 24 Stunden, für eine Update-Meldung genügt einmal am Tag.
    /// </summary>
    [Range(1, MaxCheckIntervalHours)]
    public int CheckIntervalHours { get; set; } = 24;

    /// <summary>
    /// Timeout der HTTP-Anfrage an die GitHub-API in Sekunden. Default: 10 Sekunden; die Antwort
    /// ist ein kleines JSON-Dokument, und eine hängende Leitung soll die Prüfung nicht länger aufhalten.
    /// </summary>
    [Range(1, MaxRequestTimeoutSeconds)]
    public int RequestTimeoutSeconds { get; set; } = 10;
}
