using System.ComponentModel.DataAnnotations;

namespace MdExplorer.Graph.Options;

/// <summary>
/// Konfiguration des Graph-Moduls. Wird beim Hosten via <c>ValidateDataAnnotations</c> + <c>ValidateOnStart</c> geprüft.
/// </summary>
public sealed class GraphOptions
{
    /// <summary>Konfigurations-Sektion in <c>IConfiguration</c>.</summary>
    public const string SectionName = "Graph";

    /// <summary>
    /// Validierungs-Obergrenze für <see cref="MaxNodes"/>. Sie fängt nur Tippfehler in der
    /// Konfiguration ab; flüssig darstellen lässt sich eine solche Menge nicht.
    /// </summary>
    private const int MaxNodesUpperBound = 100_000;

    /// <summary>
    /// Ob Knoten ohne ein- oder ausgehende WikiLink-Kante in den Snapshot übernommen werden.
    /// Default <see langword="false"/>, denn bei großen Repositories mit überwiegend isolierten
    /// Dokumenten (Changelogs, Vendor-Readmes) bleibt das Bild unbrauchbar, wenn isolierte Knoten
    /// mitgerendert werden.
    /// </summary>
    public bool IncludeIsolatedNodes { get; set; }

    /// <summary>
    /// Harte Obergrenze für die Anzahl Knoten im Snapshot. Beim Überschreiten werden die
    /// Knoten nach Verbindungsgrad (eingehend + ausgehend) absteigend sortiert und die Top-N
    /// behalten. Das Graph-Fenster rechnet die Abstoßung zwischen allen Knotenpaaren, der Aufwand
    /// wächst also mit dem Quadrat der Knotenzahl. Der Default 1000 gilt als Grenze, bis zu der
    /// das Bild noch flüssig auf Zoom und Verschieben reagiert.
    /// </summary>
    [Range(1, MaxNodesUpperBound)]
    public int MaxNodes { get; set; } = 1000;

    /// <summary>
    /// Glob-Muster (relativ zum Root, Trennzeichen <c>/</c>) für Dateien, die nie in den Graph
    /// einfließen sollen. Wirkt analog zu den Indexer-Ausschlussmustern. Die Defaults nehmen
    /// fremde Pakete (<c>vendor</c>, <c>node_modules</c>), mitgelieferte Changelogs unter
    /// <c>_core</c> und abgelegte Altstände (<c>alt</c>) heraus: Dokumente, die keine WikiLinks
    /// tragen und das Bild nur mit unverbundenen Punkten füllen. Ohne <c>**/</c> davor greifen
    /// die Muster nur direkt unter der Wurzel. Andere Dateiendungen als <c>.md</c> brauchen kein
    /// Muster, der Indexer nimmt sie gar nicht erst auf.
    /// </summary>
    public IList<string> PathExclusions { get; } =
    [
        "vendor/**",
        "node_modules/**",
        "_core/**/changelog/**",
        "alt/**",
    ];
}
