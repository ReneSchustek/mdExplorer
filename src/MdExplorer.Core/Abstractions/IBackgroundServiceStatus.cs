namespace MdExplorer.Core.Abstractions;

/// <summary>Die Hintergrunddienste, deren Anhalten die Betriebsanzeige meldet.</summary>
public enum BackgroundServiceKind
{
    /// <summary>Der Indexer, der Dateien erfasst und überwacht.</summary>
    Indexer,

    /// <summary>Der Parser, der Dokumente und Schlagworte aufbereitet.</summary>
    Parser,

    /// <summary>Die Pflege des Volltext-Suchindex.</summary>
    SearchIndex,

    /// <summary>Die Aktualisierung der Tag-Cloud.</summary>
    TagCloud,
}

/// <summary>
/// Hält fest, welche Hintergrunddienste nach einem unerwarteten Fehler angehalten haben.
/// </summary>
/// <remarks>
/// Ein solcher Dienst läuft bis zum Neustart nicht wieder an: Die Suche liefert veraltete Treffer,
/// die Wolke steht still. Das Protokoll allein verlöre den Fehler, sobald neuere Einträge ihn
/// verdrängen; hier bleibt er stehen, bis die Anwendung neu startet.
/// </remarks>
public interface IBackgroundServiceStatus
{
    /// <summary>Die angehaltenen Dienste.</summary>
    IReadOnlyCollection<BackgroundServiceKind> Stopped { get; }

    /// <summary>Wird gefeuert, sobald ein weiterer Dienst angehalten hat.</summary>
    event EventHandler? Changed;

    /// <summary>Meldet, dass ein Dienst nach einem unerwarteten Fehler angehalten hat.</summary>
    /// <param name="service">Der Dienst.</param>
    void ReportStopped(BackgroundServiceKind service);
}
