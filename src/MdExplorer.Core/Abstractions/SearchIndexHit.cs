namespace MdExplorer.Core.Abstractions;

/// <summary>Ein Treffer einer <see cref="SearchIndexQuery"/>.</summary>
/// <param name="MarkdownFileId">Identität des Dokuments.</param>
/// <param name="Path">Pfad-Spalte (relativ).</param>
/// <param name="Title">Titel-Spalte.</param>
/// <param name="Snippet">Vom FTS5-Snippet erzeugter Auszug, Trefferstellen zwischen den <see cref="SnippetMarkers"/>.</param>
/// <param name="Score">bm25-Score; kleiner = relevanter.</param>
public sealed record SearchIndexHit(
    Guid MarkdownFileId,
    string Path,
    string Title,
    string Snippet,
    double Score);
