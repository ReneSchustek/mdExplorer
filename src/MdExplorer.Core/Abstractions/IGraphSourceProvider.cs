namespace MdExplorer.Core.Abstractions;

/// <summary>
/// Liefert die rohen Daten, aus denen der Graph-Service einen Snapshot baut.
/// Implementierung liegt in der Data-Schicht (EF-Core), damit das Graph-Modul
/// EF-frei bleibt.
/// </summary>
public interface IGraphSourceProvider
{
    /// <summary>
    /// Lädt alle für den Graphen benötigten Rohdaten in einem Roundtrip.
    /// Reihenfolge ist deterministisch über <see cref="GraphSourceFile.Id"/>.
    /// </summary>
    Task<GraphSourceData> LoadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lädt nur die Datei-Stammdaten — ohne die Verweis-Listen der Dokumente.
    /// </summary>
    /// <remarks>
    /// Drei kurze Spalten je Datei. Die teure Hälfte des Snapshots sind die Verweis-Listen,
    /// und die braucht nicht, wer nur die Auflösung von Name auf Datei aufbauen will.
    /// </remarks>
    Task<IReadOnlyList<GraphSourceFile>> LoadFilesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lädt die Verweis-Listen, die für die Verbindungen eines einzelnen Dokuments zählen:
    /// die des Dokuments selbst und die der Dokumente, die es nennen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Der Rückweg ist als Textsuche über die Verweis-Liste vorgefiltert. Das ist ein
    /// <b>Vorfilter</b> und keine Antwort: Er kann zu viel liefern, niemals zu wenig. Wer die
    /// Kante daraus baut, muss die Liste ohnehin auswerten und verwirft die Fehltreffer dabei.
    /// </para>
    /// <para>
    /// Ein Slug besteht nach <c>ITagNormalizer</c> nur aus Buchstaben, Ziffern und
    /// Bindestrichen — Platzhalterzeichen einer Textsuche können darin nicht vorkommen.
    /// </para>
    /// </remarks>
    /// <param name="markdownFileId">Das Dokument, um dessen Verbindungen es geht.</param>
    /// <param name="targetSlug">Sein Slug — danach suchen die Verweis-Listen der anderen.</param>
    /// <param name="cancellationToken">Abbruchsteuerung.</param>
    Task<IReadOnlyList<GraphSourceDocument>> LoadNeighborhoodDocumentsAsync(
        Guid markdownFileId,
        string targetSlug,
        CancellationToken cancellationToken);
}

/// <summary>Rohdaten, aus denen ein Graph-Snapshot abgeleitet wird.</summary>
/// <param name="Files">Alle indizierten Markdown-Dateien (Id, Titel, RelativePath).</param>
/// <param name="Documents">Alle geparsten Dokumente (MarkdownFileId, OutlinksJson).</param>
public sealed record GraphSourceData(
    IReadOnlyList<GraphSourceFile> Files,
    IReadOnlyList<GraphSourceDocument> Documents);

/// <summary>Minimal-Repräsentation einer indizierten Markdown-Datei für den Graph.</summary>
/// <param name="Id">Stabiler Schlüssel.</param>
/// <param name="FileNameWithoutExtension">Dateiname ohne Erweiterung — Basis für die Slug-Auflösung.</param>
/// <param name="RelativePath">Pfad relativ zum Root.</param>
public sealed record GraphSourceFile(
    Guid Id,
    string FileNameWithoutExtension,
    string RelativePath);

/// <summary>Minimal-Repräsentation eines geparsten Dokuments.</summary>
/// <param name="MarkdownFileId">Fremdschlüssel auf <see cref="GraphSourceFile.Id"/>.</param>
/// <param name="OutlinksJson">JSON-Array mit den WikiLink-Zielen (Slug-Form).</param>
public sealed record GraphSourceDocument(Guid MarkdownFileId, string OutlinksJson);
