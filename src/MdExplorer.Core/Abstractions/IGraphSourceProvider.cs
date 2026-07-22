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
