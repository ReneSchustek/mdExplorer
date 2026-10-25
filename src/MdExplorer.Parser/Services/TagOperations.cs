using MdExplorer.Core.Abstractions;
using MdExplorer.Parser.Abstractions;

namespace MdExplorer.Parser.Services;

/// <summary>
/// Die Umbenennungen, Zusammenführungen und Löschungen eines Aufrufs, nach Slug nachschlagbar.
/// </summary>
/// <remarks>
/// Rumpf und Frontmatter fragen dieselbe Stelle, damit eine Raute im Text und ein Eintrag in der
/// <c>tags</c>-Zeile nie unterschiedlich behandelt werden. Neue Namen werden schon hier geprüft,
/// vor dem ersten Zeichen, das sich ändert: Der Aufrufer schreibt dann keine Datei halb um.
/// </remarks>
internal sealed class TagOperations
{
    private readonly ITagNormalizer _normalizer;
    private readonly Dictionary<string, string?> _replacementBySlug;

    /// <summary>Nimmt die Operationen entgegen und prüft Schlüssel und neue Namen.</summary>
    /// <param name="normalizer">Bildet die Slugs für den Vergleich.</param>
    /// <param name="operations">Je Schlagwort der neue Name, oder <see langword="null"/> zum Löschen.</param>
    /// <exception cref="ArgumentException">Ein Schlüssel ergibt keinen Slug, oder ein neuer Name ist kein gültiges Schlagwort.</exception>
    public TagOperations(ITagNormalizer normalizer, IReadOnlyDictionary<string, string?> operations)
    {
        ArgumentNullException.ThrowIfNull(normalizer);
        ArgumentNullException.ThrowIfNull(operations);
        _normalizer = normalizer;
        _replacementBySlug = new Dictionary<string, string?>(operations.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string?> entry in operations)
        {
            _replacementBySlug[normalizer.ToSlug(entry.Key)] = ValidatedName(entry.Value);
        }
    }

    /// <summary>Ob überhaupt etwas zu tun ist.</summary>
    public bool IsEmpty => _replacementBySlug.Count == 0;

    /// <summary>Schlägt die Operation für ein Schlagwort nach.</summary>
    /// <param name="tagName">Der Name, wie er im Text steht, mit oder ohne Raute.</param>
    /// <param name="replacement">Der neue Name ohne Raute, oder <see langword="null"/> zum Löschen.</param>
    /// <returns><see langword="true"/>, wenn eine Operation das Schlagwort betrifft.</returns>
    public bool TryGet(string tagName, out string? replacement)
    {
        replacement = null;
        return _normalizer.TryToSlug(tagName.TrimStart('#'), out string slug)
            && _replacementBySlug.TryGetValue(slug, out replacement);
    }

    /// <summary>Slug eines Namens, oder der Name selbst, wenn er keinen hergibt.</summary>
    /// <param name="tagName">Der Name, mit oder ohne Raute.</param>
    /// <returns>Der Schlüssel für das Erkennen doppelter Einträge.</returns>
    public string SlugOrSelf(string tagName) =>
        _normalizer.TryToSlug(tagName.TrimStart('#'), out string slug) ? slug : tagName;

    private static string? ValidatedName(string? replacement)
    {
        if (replacement is null)
        {
            return null;
        }
        if (!HashtagPattern.IsValidName(replacement))
        {
            throw new ArgumentException(
                $"„{replacement}“ ist kein gültiger Schlagwort-Name: erst ein Buchstabe, danach nur Buchstaben, Ziffern, Bindestrich oder Unterstrich.",
                nameof(replacement));
        }
        return replacement.TrimStart('#');
    }
}
