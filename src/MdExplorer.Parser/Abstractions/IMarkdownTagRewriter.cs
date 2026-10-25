namespace MdExplorer.Parser.Abstractions;

/// <summary>
/// Schreibt Hashtag-Vorkommen in Markdown-Text um (Body + YAML-Frontmatter).
/// Wird vom Tag-Management-Service genutzt, um Rename / Merge / Delete
/// projektweit konsistent anzuwenden. Reine Funktion: keine I/O, keine Seiteneffekte.
/// </summary>
public interface IMarkdownTagRewriter
{
    /// <summary>
    /// Wendet die angegebenen Slug-Operationen auf den Rohtext an. Im Rumpf werden die Hashtags
    /// ersetzt, die der Index zählt (gleiche Regel und gleiche Pipeline wie der
    /// <c>TagExtractor</c>). Im Frontmatter werden die Einträge unter dem Schlüssel <c>tags</c> auf
    /// oberster Ebene ersetzt, in Klammer-Listen (<c>tags: [a, b]</c>), Block-Listen
    /// (<c>- a</c> in Folgezeilen) und Komma-Texten (<c>tags: a, b</c>); Lage und Kommentare
    /// liefert der YAML-Leser.
    /// </summary>
    /// <param name="original">Original-Markdown-Inhalt.</param>
    /// <param name="operations">Pro Slug: neuer Name (Rename / Merge) oder <see langword="null"/> (Delete).</param>
    /// <returns>Umgeschriebener Text, identisch zu <paramref name="original"/>, wenn keine Änderung anfällt.</returns>
    /// <exception cref="ArgumentException">Ein neuer Name ist kein gültiger Schlagwort-Name (siehe <c>HashtagPattern.IsValidName</c>).</exception>
    string Apply(string original, IReadOnlyDictionary<string, string?> operations);
}
