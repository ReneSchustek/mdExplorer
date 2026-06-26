namespace MdExplorer.App.Services;

/// <summary>
/// Schmaler Adapter zum Auflösen von WikiLink-Zielen oder absoluten Pfaden gegen den Indexer-Store.
/// Eigene Abstraktion (nicht im Indexer-Modul), damit das Auflösungs-Verhalten in der App-Schicht
/// austauschbar bleibt und ohne Datenbank in Tests faktbar ist.
/// </summary>
internal interface IDocumentLocator
{
    /// <summary>
    /// Sucht das WikiLink-Ziel anhand des Dateinamens ohne Erweiterung.
    /// Liefert <see langword="null"/>, wenn nichts gefunden wurde.
    /// </summary>
    Task<Guid?> FindByWikiLinkAsync(string wikiLinkTarget, CancellationToken cancellationToken);

    /// <summary>
    /// Loest einen vollqualifizierten Dateipfad in die zugehoerige <c>MarkdownFile.Id</c> auf.
    /// Liefert <see langword="null"/>, wenn die Datei nicht indiziert ist.
    /// </summary>
    Task<Guid?> FindByAbsolutePathAsync(string absoluteFilePath, CancellationToken cancellationToken);

    /// <summary>
    /// Liefert den absoluten Dateipfad zur angegebenen <c>MarkdownFile.Id</c>.
    /// Wird vom Editor benoetigt, der die Datei im UI direkt vom Datentraeger lesen muss.
    /// </summary>
    Task<string?> GetAbsolutePathAsync(Guid markdownFileId, CancellationToken cancellationToken);
}
