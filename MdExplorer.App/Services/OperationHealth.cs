namespace MdExplorer.App.Services;

/// <summary>
/// Aggregierter Betriebs-Status der Anwendung. Wird in der Statusleiste als Health-LED
/// angezeigt und vom <see cref="IOperationHealthProvider"/> aus Subsystem-Zustaenden
/// berechnet (Indexer, Settings, jüngste Log-Einträge).
/// </summary>
internal enum OperationHealth
{
    /// <summary>Alles im Normalbetrieb — gruene LED.</summary>
    Healthy = 0,

    /// <summary>Recoverable-Problem (z. B. fehlgeschlagene Operation) — gelbe LED.</summary>
    Warning = 1,

    /// <summary>Kritischer Fehler — rote LED, sofortiger Drill-Down in den Log-Viewer.</summary>
    Error = 2,
}
