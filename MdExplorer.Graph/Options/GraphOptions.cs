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
    /// Ob Knoten ohne ein- oder ausgehende WikiLink-Kante in den Snapshot übernommen werden.
    /// Default <see langword="false"/> — bei grossen Repositories mit überwiegend isolierten
    /// Dokumenten (Changelogs, Vendor-Readmes) bleibt das Bild unbrauchbar, wenn isolierte Knoten
    /// mitgerendert werden.
    /// </summary>
    public bool IncludeIsolatedNodes { get; set; }

    /// <summary>
    /// Harte Obergrenze für die Anzahl Knoten im Snapshot. Beim Überschreiten werden die
    /// Knoten nach Verbindungsgrad (eingehend + ausgehend) absteigend sortiert und die Top-N
    /// behalten. Default 1000 — empirisches Maximum, ab dem das Cytoscape-Force-Layout im
    /// WebView2 noch interaktiv reagiert.
    /// </summary>
    [Range(1, 100_000)]
    public int MaxNodes { get; set; } = 1000;

    /// <summary>
    /// Glob-Muster (relativ zum Root, Trennzeichen <c>/</c>) für Dateien, die nie in den Graph
    /// einfliessen sollen. Wirkt analog zu den Indexer-Ausschlussmustern. Defaults zielen auf
    /// klassische Vendor- und Generated-Pfade.
    /// </summary>
    public IList<string> PathExclusions { get; } =
    [
        "vendor/**",
        "node_modules/**",
        "_core/**/changelog/**",
        "alt/**",
        "*.bak",
    ];
}
