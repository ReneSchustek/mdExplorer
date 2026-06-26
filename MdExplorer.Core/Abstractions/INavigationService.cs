namespace MdExplorer.Core.Abstractions;

/// <summary>
/// Vermittelt Dokument-Navigation aus der UI heraus. Wird vom Preview verwendet,
/// wenn ein WikiLink (<c>mdexplorer://</c>) geklickt wird, und vom Folder-Tree,
/// wenn ein Eintrag aktiviert wird.
/// </summary>
/// <remarks>
/// Implementierungen liegen in der App-Schicht. Die Abstraktion erlaubt Tests gegen Fakes.
/// </remarks>
public interface INavigationService
{
    /// <summary>
    /// Navigiert zum Dokument mit dem angegebenen WikiLink-Ziel. Auflösung erfolgt über den
    /// Dateinamen ohne Erweiterung; bei Mehrdeutigkeit gewinnt der erste Treffer in
    /// stabiler Reihenfolge.
    /// </summary>
    /// <param name="wikiLinkTarget">Roh-Zielname aus dem WikiLink, z. B. <c>"Projekt X"</c>.</param>
    /// <param name="cancellationToken">Abbruchsteuerung.</param>
    /// <returns><see langword="true"/>, wenn ein Ziel gefunden und aktiviert wurde, sonst <see langword="false"/>.</returns>
    Task<bool> NavigateToWikiLinkAsync(string wikiLinkTarget, CancellationToken cancellationToken);

    /// <summary>Navigiert zum Dokument mit der angegebenen <c>MarkdownFile.Id</c>.</summary>
    Task<bool> NavigateToDocumentAsync(Guid markdownFileId, CancellationToken cancellationToken);
}
