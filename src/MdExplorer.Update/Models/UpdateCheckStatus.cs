namespace MdExplorer.Update.Models;

/// <summary>Ergebnis-Kategorie einer Update-Prüfung.</summary>
public enum UpdateCheckStatus
{
    /// <summary>Die installierte Version ist aktuell.</summary>
    UpToDate = 0,

    /// <summary>Eine neuere Version steht zur Verfügung.</summary>
    UpdateAvailable = 1,

    /// <summary>Die Prüfung wurde übersprungen (z. B. Throttle-Intervall noch nicht abgelaufen).</summary>
    Skipped = 2,

    /// <summary>Die Prüfung schlug fehl (kein Netz, ungültige Antwort) — bewusst nicht-fatal.</summary>
    Failed = 3,
}
