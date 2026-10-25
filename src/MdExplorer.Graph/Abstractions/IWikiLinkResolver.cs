namespace MdExplorer.Graph.Abstractions;

/// <summary>
/// Löst das Ziel eines WikiLinks auf eine indizierte Datei auf.
/// </summary>
/// <remarks>
/// Es gibt genau eine Regel dafür, und sie gilt für den Graphen wie für den Klick: Name und
/// Ziel werden als Slug verglichen, und unter gleichnamigen Dateien gewinnt die erste nach
/// relativem Pfad. Liefe die Navigation nach einer anderen Regel, öffnete ein Klick eine andere
/// Datei, als die Kante im Graphen zeigt.
/// </remarks>
public interface IWikiLinkResolver
{
    /// <summary>Liefert die Datei, auf die <paramref name="wikiLinkTarget"/> verweist.</summary>
    /// <param name="wikiLinkTarget">Das Ziel, als Slug (<c>projekt-x</c>) oder wie geschrieben (<c>Projekt X</c>).</param>
    /// <param name="cancellationToken">Abbruch.</param>
    /// <returns>Die Id der Datei, oder <see langword="null"/>, wenn keine passt.</returns>
    Task<Guid?> ResolveAsync(string wikiLinkTarget, CancellationToken cancellationToken);
}
