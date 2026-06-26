namespace MdExplorer.Core.Abstractions;

/// <summary>
/// Persistenzabstraktion für den FTS5-Volltext-Index <c>MarkdownSearchIndex</c>.
/// Kapselt das SQLite-Detail, damit Such-Maintainer und Such-Service EF- und Sqlite-frei bleiben.
/// Implementierung liegt in der Data-Schicht.
/// </summary>
public interface ISearchIndexStorage
{
    /// <summary>
    /// Liest den aktuellen Indexstand als Mapping
    /// <c>MarkdownFile.Id → SourceContentHash</c>. Wird vom Maintainer zur Differenz-Bestimmung
    /// gegen die Soll-Daten verwendet.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> LoadIndexedHashesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Wendet die übergebenen Lösch- und Insert-/Update-Operationen in einer einzigen Transaktion an.
    /// Aufrufer übergeben die Anzahl der zu löschenden Dateien (z. B. Waisen) und die neu zu
    /// indexierenden Einträge.
    /// </summary>
    Task ApplyChangesAsync(
        IReadOnlyCollection<Guid> deletes,
        IReadOnlyCollection<SearchIndexEntry> upserts,
        CancellationToken cancellationToken);

    /// <summary>
    /// Führt eine FTS5-Abfrage gegen den Index aus und liefert die Treffer in Score-Reihenfolge.
    /// </summary>
    Task<IReadOnlyList<SearchIndexHit>> QueryAsync(SearchIndexQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Lädt die Body-Spalten zu den übergebenen <paramref name="markdownFileIds"/>.
    /// Wird im RegEx-Modus für den serverseitigen Postfilter genutzt.
    /// Reihenfolge der Rückgabe ist nicht garantiert; fehlende IDs werden ausgelassen.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> LoadBodiesAsync(
        IReadOnlyCollection<Guid> markdownFileIds,
        CancellationToken cancellationToken);
}

/// <summary>Ein vollständiger Eintrag, der in den FTS5-Index geschrieben werden soll.</summary>
/// <param name="MarkdownFileId">Identität — Schlüssel zur 1:1-Verbindung mit dem Dokument.</param>
/// <param name="Title">Titel-Spalte des Index.</param>
/// <param name="Body">Plaintext-Body (z. B. Frontmatter-gestripptes Markdown).</param>
/// <param name="Tags">Leerzeichen-getrennte Tag-Namen.</param>
/// <param name="Frontmatter">Flachgeklopfter Frontmatter-Text (Key Value-Folge).</param>
/// <param name="Path">Relativer Pfad — UNINDEXED-Spalte, dient als Pfad-Filter.</param>
/// <param name="SourceContentHash">Quell-Hash zur Idempotenz-Prüfung.</param>
public sealed record SearchIndexEntry(
    Guid MarkdownFileId,
    string Title,
    string Body,
    string Tags,
    string Frontmatter,
    string Path,
    string SourceContentHash);

/// <summary>FTS5-Suchanfrage mit Pfad-Filter, Pagination und Gewichten.</summary>
/// <param name="MatchExpression">FTS5-MATCH-Ausdruck (vom <c>SearchQueryBuilder</c> aufbereitet).</param>
/// <param name="PathLikePattern">Optionaler LIKE-Pfadfilter (z. B. <c>"notes/%"</c>) oder <see langword="null"/>.</param>
/// <param name="Take">Maximale Trefferzahl.</param>
/// <param name="Skip">Anzahl zu überspringender Treffer (Pagination).</param>
/// <param name="TitleWeight">bm25-Gewicht der Titelspalte.</param>
/// <param name="BodyWeight">bm25-Gewicht der Body-Spalte.</param>
/// <param name="TagsWeight">bm25-Gewicht der Tags-Spalte.</param>
/// <param name="FrontmatterWeight">bm25-Gewicht der Frontmatter-Spalte.</param>
/// <param name="SnippetTokenCount">Anzahl Tokens für die FTS5-Snippet-Erzeugung.</param>
public sealed record SearchIndexQuery(
    string MatchExpression,
    string? PathLikePattern,
    int Take,
    int Skip,
    double TitleWeight,
    double BodyWeight,
    double TagsWeight,
    double FrontmatterWeight,
    int SnippetTokenCount);

/// <summary>Ein Treffer einer <see cref="SearchIndexQuery"/>.</summary>
/// <param name="MarkdownFileId">Identität des Dokuments.</param>
/// <param name="Path">Pfad-Spalte (relativ).</param>
/// <param name="Title">Titel-Spalte.</param>
/// <param name="Snippet">Vom FTS5-Snippet erzeugter Auszug mit <c>&lt;mark&gt;</c>-Markern.</param>
/// <param name="Score">bm25-Score — kleiner = relevanter.</param>
public sealed record SearchIndexHit(
    Guid MarkdownFileId,
    string Path,
    string Title,
    string Snippet,
    double Score);
