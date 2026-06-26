namespace MdExplorer.App.Services;

/// <summary>
/// Liefert den aggregierten Betriebs-Status der Anwendung fuer die Statusleiste.
/// Implementierungen leiten den Status aus Subsystem-Signalen ab (jüngste Log-Eintraege,
/// Indexer-Heartbeat, Settings-Service-State).
/// </summary>
internal interface IOperationHealthProvider
{
    /// <summary>Aktuelle aggregierte Bewertung.</summary>
    OperationHealth Current { get; }

    /// <summary>Begruendungstext fuer den Tooltip — eine Zeile pro Subsystem mit eigenem Status.</summary>
    string Detail { get; }

    /// <summary>Wird gefeuert, sobald sich <see cref="Current"/> oder <see cref="Detail"/> aendert.</summary>
    event EventHandler? Changed;
}
