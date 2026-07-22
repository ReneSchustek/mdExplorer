namespace MdExplorer.Core.Abstractions;

/// <summary>
/// Liefert die Rohdaten, aus denen der FTS5-Index-Maintainer den Such-Index aufbaut.
/// Implementierung liegt in der Data-Schicht (EF-Core), damit das Search-Modul
/// EF-frei bleibt — analoges Pattern zu <see cref="IGraphSourceProvider"/>.
/// </summary>
public interface ISearchSourceProvider
{
    /// <summary>
    /// Lädt alle für die FTS5-Indexpflege benötigten Daten in einem Roundtrip:
    /// Dokument-Stammdaten (Titel, Pfade, Hash, Frontmatter) und Tag-Aggregat je Datei.
    /// Reihenfolge ist deterministisch über <see cref="SearchSourceDocument.MarkdownFileId"/>.
    /// </summary>
    Task<SearchSourceData> LoadAsync(CancellationToken cancellationToken);
}

/// <summary>Rohdaten für die FTS5-Indexpflege.</summary>
/// <param name="Documents">Alle zu indizierenden Dokumente (Datei-Metadaten + Parser-Output).</param>
/// <param name="TagsByFileId">Pro <c>MarkdownFile.Id</c> die Original-Tag-Namen, leertrennbar.</param>
public sealed record SearchSourceData(
    IReadOnlyList<SearchSourceDocument> Documents,
    IReadOnlyDictionary<Guid, IReadOnlyList<string>> TagsByFileId);

/// <summary>Minimal-Repräsentation eines indizierungsrelevanten Dokuments.</summary>
/// <param name="MarkdownFileId">Fremdschlüssel auf <c>MarkdownFile.Id</c> — Identität des Index-Eintrags.</param>
/// <param name="Title">Titel — entspricht <c>MarkdownFile.FileNameWithoutExtension</c>.</param>
/// <param name="AbsolutePath">Absoluter Dateipfad — Quelle für den Body-Text.</param>
/// <param name="RelativePath">Relativer Pfad — wird als <c>Path</c>-Spalte im FTS5-Index gespeichert.</param>
/// <param name="SourceContentHash">Aktueller Quell-Hash — Idempotenz-Marker.</param>
/// <param name="FrontmatterJson">Frontmatter-JSON aus dem Parser — wird im Maintainer flachgeklopft.</param>
public sealed record SearchSourceDocument(
    Guid MarkdownFileId,
    string Title,
    string AbsolutePath,
    string RelativePath,
    string SourceContentHash,
    string FrontmatterJson);
