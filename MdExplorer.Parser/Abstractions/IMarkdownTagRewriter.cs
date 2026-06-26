namespace MdExplorer.Parser.Abstractions;

/// <summary>
/// Schreibt Hashtag-Vorkommen in Markdown-Text um (Body + YAML-Frontmatter).
/// Wird vom Tag-Management-Service genutzt, um Rename / Merge / Delete
/// projektweit konsistent anzuwenden. Reine Funktion — keine I/O, keine Seiteneffekte.
/// </summary>
public interface IMarkdownTagRewriter
{
    /// <summary>
    /// Wendet die angegebenen Slug-Operationen auf den Rohtext an. Body-Vorkommen werden
    /// per Boundary-Regex erkannt (gleiche Regel wie <c>TagExtractor</c>), Frontmatter-Eintraege
    /// im <c>tags</c>-Feld werden zeilenbasiert manipuliert. Verarbeitet Inline-Sequenzen
    /// (<c>tags: [a, b]</c>), kommagetrennte Werte (<c>tags: a, b</c>) und Block-Listen
    /// (<c>- a</c> in Folgezeilen).
    /// </summary>
    /// <param name="original">Original-Markdown-Inhalt.</param>
    /// <param name="operations">Pro Slug: neuer Name (Rename / Merge) oder <see langword="null"/> (Delete).</param>
    /// <returns>Umgeschriebener Text — identisch zu <paramref name="original"/>, wenn keine Aenderung anfaellt.</returns>
    string Apply(string original, IReadOnlyDictionary<string, string?> operations);
}
