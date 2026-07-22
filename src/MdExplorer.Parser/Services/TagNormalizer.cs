using System.Globalization;
using System.Text;
using MdExplorer.Parser.Abstractions;

namespace MdExplorer.Parser.Services;

/// <summary>
/// Erzeugt URL-/Vergleichs-Slugs aus Tag-/WikiLink-Namen.
/// Lowercase via Invariant-Culture, Whitespace und Trennzeichen werden zu Bindestrich,
/// Umlaute bleiben erhalten (Projekt-Regel — keine ae/oe/ue-Ersetzung).
/// Steuerzeichen und ASCII-Interpunktion fallen weg.
/// </summary>
public sealed class TagNormalizer : ITagNormalizer
{
    /// <inheritdoc />
    public string ToSlug(string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        if (!TryBuildSlug(raw, out string slug))
        {
            throw new ArgumentException(
                $"Eingabe '{raw}' enthält keine slug-tauglichen Zeichen.",
                nameof(raw));
        }
        return slug;
    }

    /// <inheritdoc />
    public bool TryToSlug(string? raw, out string slug)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            slug = string.Empty;
            return false;
        }
        return TryBuildSlug(raw, out slug);
    }

    private static bool TryBuildSlug(string raw, out string slug)
    {
        string trimmed = raw.Trim();
        StringBuilder builder = new(trimmed.Length);
        bool lastWasHyphen = false;

        foreach (Rune rune in trimmed.EnumerateRunes())
        {
            if (IsSlugLetterOrDigit(rune))
            {
                Rune lowered = Rune.ToLowerInvariant(rune);
                _ = builder.Append(lowered.ToString());
                lastWasHyphen = false;
                continue;
            }

            if (ShouldCollapseToHyphen(rune) && !lastWasHyphen && builder.Length > 0)
            {
                _ = builder.Append('-');
                lastWasHyphen = true;
            }
        }

        if (lastWasHyphen)
        {
            builder.Length--;
        }

        if (builder.Length == 0)
        {
            slug = string.Empty;
            return false;
        }

        slug = builder.ToString();
        return true;
    }

    private static bool IsSlugLetterOrDigit(Rune rune)
    {
        UnicodeCategory category = Rune.GetUnicodeCategory(rune);
        return category
            is UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.OtherLetter
            or UnicodeCategory.ModifierLetter
            or UnicodeCategory.DecimalDigitNumber;
    }

    private static bool ShouldCollapseToHyphen(Rune rune)
    {
        if (Rune.IsWhiteSpace(rune))
        {
            return true;
        }
        return rune.Value is '-' or '_';
    }
}
