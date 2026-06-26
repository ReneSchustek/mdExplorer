namespace MdExplorer.Update.Models;

/// <summary>
/// Ergebnis einer Update-Prüfung. Trägt die installierte und — sofern ermittelt — die
/// neueste Version sowie die Release-URL für die Benachrichtigung.
/// </summary>
/// <param name="Status">Kategorie des Ergebnisses.</param>
/// <param name="CurrentVersion">Die installierte (laufende) Version.</param>
/// <param name="LatestVersion">Die neueste veröffentlichte Version, oder <see langword="null"/>, wenn nicht ermittelt.</param>
/// <param name="ReleaseUrl">Browser-URL der neuesten Release-Seite, oder <see langword="null"/>.</param>
public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    SemanticVersion CurrentVersion,
    SemanticVersion? LatestVersion,
    Uri? ReleaseUrl)
{
    /// <summary><see langword="true"/>, wenn eine neuere Version verfügbar ist.</summary>
    public bool IsUpdateAvailable => Status == UpdateCheckStatus.UpdateAvailable;

    /// <summary>Liefert ein <see cref="UpdateCheckStatus.UpToDate"/>-Ergebnis.</summary>
    public static UpdateCheckResult UpToDate(SemanticVersion current, SemanticVersion latest) =>
        new(UpdateCheckStatus.UpToDate, current, latest, null);

    /// <summary>Liefert ein <see cref="UpdateCheckStatus.UpdateAvailable"/>-Ergebnis.</summary>
    public static UpdateCheckResult Available(SemanticVersion current, SemanticVersion latest, Uri releaseUrl) =>
        new(UpdateCheckStatus.UpdateAvailable, current, latest, releaseUrl);

    /// <summary>Liefert ein <see cref="UpdateCheckStatus.Skipped"/>-Ergebnis.</summary>
    public static UpdateCheckResult Skipped(SemanticVersion current) =>
        new(UpdateCheckStatus.Skipped, current, null, null);

    /// <summary>Liefert ein <see cref="UpdateCheckStatus.Failed"/>-Ergebnis.</summary>
    public static UpdateCheckResult Failed(SemanticVersion current) =>
        new(UpdateCheckStatus.Failed, current, null, null);
}
