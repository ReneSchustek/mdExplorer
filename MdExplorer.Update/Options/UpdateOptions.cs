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

    /// <summary>GitHub-Eigentümer (Benutzer/Organisation) des Repositories.</summary>
    [Required]
    public string RepositoryOwner { get; set; } = "ReneSchustek";

    /// <summary>Name des GitHub-Repositories.</summary>
    [Required]
    public string RepositoryName { get; set; } = "mdExplorer";

    /// <summary>
    /// Mindestabstand zwischen zwei tatsächlichen Netzprüfungen in Stunden. Verhindert, dass
    /// häufige Programmstarts die GitHub-API unnötig belasten. Default: 24 Stunden.
    /// </summary>
    [Range(1, 24 * 7)]
    public int CheckIntervalHours { get; set; } = 24;

    /// <summary>Timeout der HTTP-Anfrage an die GitHub-API in Sekunden. Default: 10 Sekunden.</summary>
    [Range(1, 60)]
    public int RequestTimeoutSeconds { get; set; } = 10;
}
