namespace MdExplorer.Parser.Abstractions;

/// <summary>
/// Normalisiert Tag-/WikiLink-Namen zu Slugs: Lowercase, Whitespace → Bindestrich, Umlaute bleiben erhalten.
/// </summary>
public interface ITagNormalizer
{
    /// <summary>Erzeugt den Slug für einen Tag-/Linkname. Wirft, wenn kein slug-taugliches Zeichen enthalten ist.</summary>
    string ToSlug(string raw);

    /// <summary>
    /// Nicht-werfende Variante von <see cref="ToSlug"/>: liefert <see langword="false"/>, wenn
    /// <paramref name="raw"/> leer/whitespace ist oder kein slug-taugliches Zeichen enthält
    /// (z. B. ein Dateiname wie <c>#.md</c>). <paramref name="slug"/> ist dann <see cref="string.Empty"/>.
    /// </summary>
    /// <param name="raw">Roh-Eingabe (Tag- oder WikiLink-Name).</param>
    /// <param name="slug">Der erzeugte Slug bei Erfolg, sonst leer.</param>
    /// <returns><see langword="true"/>, wenn ein Slug gebildet werden konnte.</returns>
    bool TryToSlug(string? raw, out string slug);
}
