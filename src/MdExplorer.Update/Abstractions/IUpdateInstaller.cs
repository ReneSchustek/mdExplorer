using MdExplorer.Update.Models;

namespace MdExplorer.Update.Abstractions;

/// <summary>
/// Lädt das Installationspaket eines Releases herunter, prüft es gegen den veröffentlichten
/// SHA-256-Wert und startet es. Implementierungen arbeiten fehlertolerant: Netz- und
/// Dateisystemfehler werden zu einem Ergebnis mit Begründung, nicht zu einer Ausnahme.
/// </summary>
public interface IUpdateInstaller
{
    /// <summary>
    /// Lädt <paramref name="asset"/> herunter und verifiziert es.
    /// </summary>
    /// <param name="asset">Das zu ladende Paket; muss einen Prüfwert tragen.</param>
    /// <param name="progress">Fortschritt in Prozent (0–100), optional.</param>
    /// <param name="cancellationToken">Abbruch-Token; wird kooperativ beachtet.</param>
    /// <returns>Ergebnis mit dem lokalen Pfad bei Erfolg, sonst mit Fehlergrund.</returns>
    Task<UpdateDownloadResult> DownloadAndVerifyAsync(
        UpdateAsset asset,
        IProgress<int>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Startet das zuvor geprüfte Installationspaket in einem eigenen Prozess.
    /// </summary>
    /// <param name="installerPath">Pfad aus einem erfolgreichen <see cref="DownloadAndVerifyAsync"/>.</param>
    /// <returns><see langword="true"/>, wenn der Start gelang.</returns>
    /// <remarks>
    /// Beendet die Anwendung <b>nicht</b> selbst — das entscheidet die Oberfläche, die noch
    /// ungespeicherte Arbeit kennt.
    /// </remarks>
    bool StartInstaller(string installerPath);
}
