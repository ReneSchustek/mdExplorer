using System.ComponentModel.DataAnnotations;

namespace MdExplorer.Search.Options;

/// <summary>
/// Konfiguration des Such-Moduls. Wird beim Hosten via <c>ValidateDataAnnotations</c> + <c>ValidateOnStart</c> geprüft.
/// </summary>
public sealed class SearchOptions
{
    /// <summary>Konfigurations-Sektion in <c>IConfiguration</c>.</summary>
    public const string SectionName = "Search";

    /// <summary>Obergrenze für alle BM25-Spalten-Gewichte.</summary>
    private const double MaxColumnWeight = 100.0;

    /// <summary>Obergrenze für Treffer-/Take-Anzahlen (harte Ergebnis-Kappung).</summary>
    private const int MaxResultCount = 10_000;

    /// <summary>Obergrenze für die Snippet-Token-Anzahl.</summary>
    private const int MaxSnippetTokenCount = 64;

    /// <summary>Obergrenze für das Maintenance-Polling-Intervall (Sekunden).</summary>
    private const int MaxMaintenanceIntervalSeconds = 600;

    /// <summary>Obergrenze für die Anzahl Dokumente pro Maintenance-Durchlauf.</summary>
    private const int MaxMaintenanceBatchSize = 10_000;

    /// <summary>Obergrenze für die Anzahl materialisierter RegEx-Vorfilter-Kandidaten.</summary>
    private const int MaxRegexCandidateLimit = 100_000;

    /// <summary>Untergrenze für das RegEx-Postfilter-Timeout (Millisekunden).</summary>
    private const int MinRegexTimeoutMs = 10;

    /// <summary>Obergrenze für das RegEx-Postfilter-Timeout (Millisekunden).</summary>
    private const int MaxRegexTimeoutMs = 60_000;

    /// <summary>Obergrenze für die Tokenanzahl einer expandierten Similarity-Match-Expression.</summary>
    private const int MaxExpandedTokenCount = 1_024;

    /// <summary>Obergrenze für das NEAR-Proximity-Fenster.</summary>
    private const int MaxNearProximityWindow = 100;

    /// <summary>Spalten-Gewicht für <c>Title</c> im BM25-Ranking. Default: 3.0.</summary>
    [Range(0.0, MaxColumnWeight)]
    public double TitleWeight { get; set; } = 3.0;

    /// <summary>Spalten-Gewicht für <c>Body</c> im BM25-Ranking. Default: 1.0.</summary>
    [Range(0.0, MaxColumnWeight)]
    public double BodyWeight { get; set; } = 1.0;

    /// <summary>Spalten-Gewicht für <c>Tags</c> im BM25-Ranking. Default: 2.0.</summary>
    [Range(0.0, MaxColumnWeight)]
    public double TagsWeight { get; set; } = 2.0;

    /// <summary>Spalten-Gewicht für <c>Frontmatter</c> im BM25-Ranking. Default: 1.0.</summary>
    [Range(0.0, MaxColumnWeight)]
    public double FrontmatterWeight { get; set; } = 1.0;

    /// <summary>Maximale Anzahl Treffer pro Anfrage (harte Obergrenze). Default: 100.</summary>
    [Range(1, MaxResultCount)]
    public int MaxResults { get; set; } = 100;

    /// <summary>Default-Trefferzahl, wenn die Anfrage keinen oder einen ungültigen Wert liefert. Default: 20.</summary>
    [Range(1, MaxResultCount)]
    public int DefaultTake { get; set; } = 20;

    /// <summary>Snippet-Token-Anzahl für die FTS5-<c>snippet()</c>-Funktion. Default: 16 Tokens.</summary>
    [Range(1, MaxSnippetTokenCount)]
    public int SnippetTokenCount { get; set; } = 16;

    /// <summary>Pollingintervall (in Sekunden), in dem der Maintainer den FTS5-Index synchronisiert. Default: 5 s.</summary>
    [Range(1, MaxMaintenanceIntervalSeconds)]
    public int IndexMaintenanceIntervalSeconds { get; set; } = 5;

    /// <summary>Maximale Anzahl Dokumente pro Maintenance-Durchlauf. Default: 200.</summary>
    [Range(1, MaxMaintenanceBatchSize)]
    public int MaintenanceBatchSize { get; set; } = 200;

    /// <summary>
    /// Obergrenze für die Anzahl FTS5-Vorfilter-Treffer, die im RegEx-Modus
    /// gegen das Pattern materialisiert werden. Default 5000 — schützt
    /// vor RAM-Explosion bei zu weichen Patterns wie <c>.+</c>.
    /// </summary>
    [Range(1, MaxRegexCandidateLimit)]
    public int MaxRegexCandidates { get; set; } = 5_000;

    /// <summary>
    /// Timeout in Millisekunden für einen einzelnen <see cref="System.Text.RegularExpressions.Regex.IsMatch(string)"/>-
    /// Aufruf im RegEx-Postfilter. Schutz gegen Catastrophic-Backtracking (ReDoS). Default 200 ms.
    /// </summary>
    [Range(MinRegexTimeoutMs, MaxRegexTimeoutMs)]
    public int RegexTimeoutMs { get; set; } = 200;

    /// <summary>
    /// Maximale Tokenanzahl in einer expandierten Similarity-Match-Expression.
    /// Verhindert, dass Stemming + Synonyme die FTS5-Anfrage in eine 100+-Term-OR-Liste sprengen.
    /// </summary>
    [Range(1, MaxExpandedTokenCount)]
    public int MaxExpandedTokens { get; set; } = 32;

    /// <summary>
    /// Token-Distanz für den FTS5-<c>NEAR</c>-Operator im Similarity-Modus.
    /// Default 5 — wirkt wie ein „im selben Satz"-Filter.
    /// </summary>
    [Range(1, MaxNearProximityWindow)]
    public int NearProximityWindow { get; set; } = 5;

    /// <summary>
    /// Optionaler Pfad zur Synonym-Datei (Schema: <c>Dictionary&lt;string, string[]&gt;</c>).
    /// Bei <see langword="null"/> oder fehlender Datei läuft der Synonym-Modus ohne Erweiterung.
    /// </summary>
    public string? SynonymFilePath { get; set; }
}
