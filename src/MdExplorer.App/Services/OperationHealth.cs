namespace MdExplorer.App.Services;

/// <summary>
/// Aggregierter Betriebs-Status der Anwendung. Wird in der Statusleiste als Health-LED
/// angezeigt und vom <see cref="IOperationHealthProvider"/> berechnet (jüngste Log-Einträge,
/// nicht verarbeitbare Dateien, angehaltene Hintergrunddienste).
/// </summary>
internal enum OperationHealth
{
    /// <summary>Alles im Normalbetrieb, grüne LED.</summary>
    Healthy = 0,

    /// <summary>Warnungen im Beobachtungsfenster oder nicht verarbeitbare Dateien, gelbe LED.</summary>
    Warning = 1,

    /// <summary>Mindestens ein Fehler im Beobachtungsfenster, rote LED. Ein Klick auf die LED öffnet in jedem Zustand den Log-Viewer.</summary>
    Error = 2,
}
