namespace MdExplorer.TagCloud.Abstractions;

/// <summary>
/// Projektweite Tag-Verwaltung: Vorschau, Umbenennen, Mergen, Loeschen. Operationen
/// schreiben die Quelldateien atomar (Temp + Move via <see cref="Core.Abstractions.IFileSystem"/>)
/// und ueberlassen die DB-Aktualisierung dem regulaeren Indexer-Re-Scan, der vom
/// <c>FileSystemWatcher</c> getriggert wird.
/// </summary>
public interface ITagManagementService
{
    /// <summary>
    /// Liefert die Liste der Dateien, die den angegebenen Slug referenzieren — fuer die
    /// Vorschau im Tag-Management-Fenster.
    /// </summary>
    Task<TagPreview> GetPreviewAsync(string slug, CancellationToken cancellationToken);

    /// <summary>
    /// Benennt den Tag global um. Body-Vorkommen werden ueber die Slug-Identitaet erkannt
    /// (alle Schreibvarianten, deren Slug <paramref name="oldSlug"/> entspricht). Frontmatter-
    /// Listen werden konsistent angepasst.
    /// </summary>
    Task<TagRewriteResult> RenameAsync(string oldSlug, string newRawName, CancellationToken cancellationToken);

    /// <summary>
    /// Fuehrt <paramref name="sourceSlug"/> in <paramref name="targetRawName"/> ueber. Der Quell-Tag
    /// verschwindet aus allen Dateien; in jeder betroffenen Datei wird das Vorkommen durch
    /// <paramref name="targetRawName"/> ersetzt. Duplikate im Frontmatter werden vermieden.
    /// </summary>
    Task<TagRewriteResult> MergeAsync(string sourceSlug, string targetRawName, CancellationToken cancellationToken);

    /// <summary>
    /// Entfernt den Tag ueberall: Body-Vorkommen werden geloescht, Frontmatter-Eintraege ebenfalls.
    /// Nach dem Re-Index werden verwaiste Tag-Eintraege durch den FK-Cascade entfernt.
    /// </summary>
    Task<TagRewriteResult> DeleteAsync(string slug, CancellationToken cancellationToken);
}

/// <summary>Vorschau einer Tag-Operation — Anzahl + erste paar Dateipfade.</summary>
/// <param name="Slug">Betroffener Slug.</param>
/// <param name="FileCount">Anzahl betroffener Dateien.</param>
/// <param name="SamplePaths">Erste 10 relativen Pfade (alphabetisch) fuer die UI.</param>
public sealed record TagPreview(string Slug, int FileCount, IReadOnlyList<string> SamplePaths);

/// <summary>Ergebnis einer Schreiboperation.</summary>
/// <param name="Slug">Bearbeiteter Slug.</param>
/// <param name="FilesAffected">Anzahl Dateien, in denen tatsaechlich geschrieben wurde.</param>
/// <param name="FilesAttempted">Anzahl Dateien, die laut Index potenziell betroffen waren.</param>
/// <param name="Errors">Fehler je Datei (Pfad → Meldung). Leer = vollstaendig erfolgreich.</param>
public sealed record TagRewriteResult(
    string Slug,
    int FilesAffected,
    int FilesAttempted,
    IReadOnlyDictionary<string, string> Errors);
