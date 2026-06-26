namespace MdExplorer.Parser.Abstractions;

/// <summary>
/// Normalisiert Tag-/WikiLink-Namen zu Slugs: Lowercase, Whitespace → Bindestrich, Umlaute bleiben erhalten.
/// </summary>
public interface ITagNormalizer
{
    /// <summary>Erzeugt den Slug für einen Tag-/Linkname.</summary>
    string ToSlug(string raw);
}
