namespace MdExplorer.TagCloud.Abstractions;

/// <summary>Ergebnis einer Schreiboperation.</summary>
/// <param name="Slug">Bearbeiteter Slug.</param>
/// <param name="FilesAffected">Anzahl Dateien, in denen tatsächlich geschrieben wurde.</param>
/// <param name="FilesAttempted">Anzahl Dateien, die laut Index potenziell betroffen waren.</param>
/// <param name="Errors">Fehler je Datei (Pfad → Meldung). Leer = vollständig erfolgreich.</param>
public sealed record TagRewriteResult(
    string Slug,
    int FilesAffected,
    int FilesAttempted,
    IReadOnlyDictionary<string, string> Errors);
