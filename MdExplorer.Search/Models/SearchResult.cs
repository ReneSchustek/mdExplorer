namespace MdExplorer.Search.Models;

/// <summary>
/// Einzelner Suchtreffer aus dem FTS5-Index. Der Score entspricht dem BM25-Wert
/// (kleiner = besser nach FTS5-Konvention; der Service sortiert aufsteigend).
/// </summary>
/// <param name="MarkdownFileId">Schlüssel der Markdown-Datei (Schlüssel auf <c>MarkdownFiles.Id</c>).</param>
/// <param name="Path">Pfad-Wert der Datei (entspricht <c>MarkdownFile.RelativePath</c>).</param>
/// <param name="Title">Titel-Wert (entspricht <c>MarkdownFile.FileNameWithoutExtension</c>).</param>
/// <param name="Score">BM25-Score — kleinere Werte bedeuten relevantere Treffer.</param>
/// <param name="Snippet">HTML-Snippet mit <c>&lt;mark&gt;</c>-Markern um die Trefferstellen.</param>
/// <param name="Highlights">Positionen der Highlights innerhalb von <paramref name="Snippet"/>.</param>
public sealed record SearchResult(
    Guid MarkdownFileId,
    string Path,
    string Title,
    double Score,
    string Snippet,
    IReadOnlyList<SearchHighlight> Highlights);
