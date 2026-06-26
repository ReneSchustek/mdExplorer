namespace MdExplorer.Core.Abstractions;

/// <summary>
/// Liefert die Liste der Markdown-Dateien, die einen bestimmten Tag-Slug referenzieren.
/// Wird vom Tag-Management-Service für Rename / Merge / Delete-Operationen genutzt:
/// pro Slug die betroffenen Pfade einmalig laden, Dateien atomar neuschreiben,
/// der Indexer-Watcher uebernimmt die Re-Sync.
/// </summary>
public interface ITagFileLookupQuery
{
    /// <summary>
    /// Liefert alle Dateien, deren Tag-Set den angegebenen Slug enthaelt. Ergebnis ist
    /// nach <see cref="TagFileLookupRow.RelativePath"/> sortiert, damit Vorschauen
    /// stabil bleiben.
    /// </summary>
    /// <param name="slug">Der eindeutige Tag-Slug (lowercase, vgl. <c>Tag.Slug</c>).</param>
    /// <param name="cancellationToken">Abbruchsteuerung.</param>
    Task<IReadOnlyList<TagFileLookupRow>> GetFilesByTagSlugAsync(string slug, CancellationToken cancellationToken);
}

/// <summary>Aggregat-Zeile fuer <see cref="ITagFileLookupQuery"/>.</summary>
/// <param name="MarkdownFileId">Primaerschluessel der betroffenen Datei.</param>
/// <param name="AbsolutePath">Vollstaendiger Pfad im Datei-System.</param>
/// <param name="RelativePath">Pfad relativ zum Index-Root — fuer Anzeige.</param>
public sealed record TagFileLookupRow(Guid MarkdownFileId, string AbsolutePath, string RelativePath);
