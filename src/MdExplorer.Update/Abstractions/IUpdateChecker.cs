using MdExplorer.Update.Models;

namespace MdExplorer.Update.Abstractions;

/// <summary>
/// Prüft, ob eine neuere Version der Anwendung veröffentlicht wurde. Implementierungen
/// arbeiten bewusst fehlertolerant: fehlt das Netz oder antwortet die Quelle ungültig,
/// liefern sie <see cref="UpdateCheckStatus.Failed"/> statt eine Ausnahme zu werfen.
/// </summary>
public interface IUpdateChecker
{
    /// <summary>Führt die Prüfung aus (inklusive Throttle-Logik) und liefert das Ergebnis.</summary>
    /// <param name="cancellationToken">Abbruch-Token; wird kooperativ beachtet.</param>
    /// <returns>Das Prüfergebnis, niemals <see langword="null"/>.</returns>
    Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Führt die Prüfung aus und übergeht dabei auf Wunsch die Drossel.
    /// </summary>
    /// <param name="force">
    /// <see langword="true"/> für eine vom Nutzer ausgelöste Prüfung. Die Drossel schützt die
    /// GitHub-API vor häufigen Programmstarts — wer den Knopf drückt, erwartet dagegen eine
    /// echte Abfrage und keine stille Auskunft von gestern.
    /// </param>
    /// <param name="cancellationToken">Abbruch-Token; wird kooperativ beachtet.</param>
    /// <returns>Das Prüfergebnis, niemals <see langword="null"/>.</returns>
    Task<UpdateCheckResult> CheckForUpdateAsync(bool force, CancellationToken cancellationToken);
}
