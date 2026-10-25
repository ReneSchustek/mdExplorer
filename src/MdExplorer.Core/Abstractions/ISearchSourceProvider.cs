namespace MdExplorer.Core.Abstractions;

/// <summary>
/// Liefert die Rohdaten, aus denen der FTS5-Index-Maintainer den Such-Index aufbaut.
/// Implementierung liegt in der Data-Schicht (EF-Core), damit das Search-Modul
/// EF-frei bleibt. Analoges Pattern zu <see cref="IGraphSourceProvider"/>.
/// </summary>
public interface ISearchSourceProvider
{
    /// <summary>
    /// Lädt alle für die FTS5-Indexpflege benötigten Daten in zwei Abfragen:
    /// Dokument-Stammdaten (Titel, Pfade, Hash, Frontmatter) und die Tag-Namen je Datei.
    /// Die Dokumente sind nach <see cref="SearchSourceDocument.MarkdownFileId"/> sortiert.
    /// </summary>
    Task<SearchSourceData> LoadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Liefert einen günstigen Änderungsmarker des Bestands: Anzahl und jüngster Zeitstempel der
    /// Dokumente und der Dateien. Ändert sich weder Inhalt noch Pfad noch Bestand, bleibt er gleich.
    /// </summary>
    Task<SearchSourceVersion> LoadVersionAsync(CancellationToken cancellationToken);
}

/// <summary>Änderungsmarker des Bestands, aus dem der Suchindex aufgebaut wird.</summary>
/// <param name="DocumentCount">Anzahl geparster Dokumente.</param>
/// <param name="LatestParsedAtUtc">Jüngster Parse-Zeitpunkt; neuer Inhalt oder neue Schlagworte setzen ihn.</param>
/// <param name="FileCount">Anzahl indizierter Dateien.</param>
/// <param name="LatestIndexedAtUtc">Jüngste Änderung an einem Dateieintrag; auch ein Umbenennen setzt sie.</param>
public readonly record struct SearchSourceVersion(
    int DocumentCount,
    DateTime? LatestParsedAtUtc,
    int FileCount,
    DateTime? LatestIndexedAtUtc);

/// <summary>Rohdaten für die FTS5-Indexpflege.</summary>
/// <param name="Documents">Alle zu indizierenden Dokumente (Datei-Metadaten + Parser-Output).</param>
/// <param name="TagsByFileId">Pro <c>MarkdownFile.Id</c> die Original-Tag-Namen; der Maintainer fügt sie mit Leerzeichen zur Tags-Spalte zusammen.</param>
public sealed record SearchSourceData(
    IReadOnlyList<SearchSourceDocument> Documents,
    IReadOnlyDictionary<Guid, IReadOnlyList<string>> TagsByFileId);

/// <summary>Minimal-Repräsentation eines indizierungsrelevanten Dokuments.</summary>
/// <param name="MarkdownFileId">Fremdschlüssel auf <c>MarkdownFile.Id</c> als Identität des Index-Eintrags.</param>
/// <param name="Title">Titel, entspricht <c>MarkdownFile.FileNameWithoutExtension</c>.</param>
/// <param name="AbsolutePath">Absoluter Dateipfad als Quelle für den Body-Text.</param>
/// <param name="RelativePath">Relativer Pfad, wird als <c>Path</c>-Spalte im FTS5-Index gespeichert.</param>
/// <param name="SourceContentHash">Aktueller Quell-Hash (Idempotenz-Marker).</param>
/// <param name="FrontmatterJson">Frontmatter-JSON aus dem Parser. Wird im Maintainer flachgeklopft.</param>
public sealed record SearchSourceDocument(
    Guid MarkdownFileId,
    string Title,
    string AbsolutePath,
    string RelativePath,
    string SourceContentHash,
    string FrontmatterJson);
