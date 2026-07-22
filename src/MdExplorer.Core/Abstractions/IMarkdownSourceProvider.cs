namespace MdExplorer.Core.Abstractions;

/// <summary>
/// Liefert die für das Parsing relevanten Snapshot-Daten der indizierten Markdown-Dateien
/// (Id, Pfad, aktueller Inhalt-Hash). Implementierung in der Data-Schicht — der Parser bleibt
/// EF-frei und kennt nur diese Abstraktion.
/// </summary>
public interface IMarkdownSourceProvider
{
    /// <summary>Liefert alle indizierten Markdown-Dateien als Streaming-Sicht.</summary>
    IAsyncEnumerable<MarkdownSourceSnapshot> EnumerateAsync(CancellationToken cancellationToken);
}

/// <summary>Read-Only-Sicht auf eine indizierte Markdown-Datei für den Parse-Orchestrator.</summary>
/// <param name="Id">Primärschlüssel der Datei (entspricht <c>MarkdownFile.Id</c>).</param>
/// <param name="AbsolutePath">Vollständiger Dateipfad zum Lesen des Inhalts.</param>
/// <param name="ContentHash">SHA-256 des Datei-Inhalts (aus dem Indexer).</param>
public sealed record MarkdownSourceSnapshot(Guid Id, string AbsolutePath, string ContentHash);
