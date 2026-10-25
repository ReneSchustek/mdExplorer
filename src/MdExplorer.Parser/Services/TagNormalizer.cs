using System.Globalization;
using System.Text;
using MdExplorer.Core.Abstractions;
using MdExplorer.Parser.Abstractions;

namespace MdExplorer.Parser.Services;

/// <summary>
/// Erzeugt URL-/Vergleichs-Slugs aus Tag-/WikiLink-Namen.
/// Buchstaben und Ziffern werden invariant kleingeschrieben, Umlaute bleiben erhalten und werden nicht
/// umschrieben. Leerraum, Bindestrich und Unterstrich werden zu einem Bindestrich; alles andere
/// (Satzzeichen, Symbole, Steuerzeichen) fällt weg.
/// </summary>
/// <remarks>
/// Vorher wird der Text in die zusammengesetzte Unicode-Form (NFC) gebracht. macOS und manche
/// Editoren speichern „ä“ als „a“ mit nachgestellten Punkten; ohne den Schritt fiele der
/// Punkt als Zeichen ohne Buchstabenwert weg, und aus „Bär“ würde „bar“, ein anderes Schlagwort.
/// </remarks>
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
        string composed = trimmed.IsNormalized(NormalizationForm.FormC) ? trimmed : trimmed.Normalize(NormalizationForm.FormC);
        StringBuilder builder = new(composed.Length);
        foreach (Rune rune in composed.EnumerateRunes())
        {
            AppendRune(builder, rune);
        }

        if (builder.Length > 0 && builder[^1] == '-')
        {
            builder.Length--;
        }

        slug = builder.ToString();
        return slug.Length > 0;
    }

    // Buchstaben und Ziffern kommen kleingeschrieben hinein; Trenner werden zu genau einem
    // Bindestrich, nie am Anfang und nie doppelt.
    private static void AppendRune(StringBuilder builder, Rune rune)
    {
        if (IsSlugLetterOrDigit(rune))
        {
            Span<char> utf16 = stackalloc char[2];
            int written = Rune.ToLowerInvariant(rune).EncodeToUtf16(utf16);
            _ = builder.Append(utf16[..written]);
            return;
        }

        if (ShouldCollapseToHyphen(rune) && builder.Length > 0 && builder[^1] != '-')
        {
            _ = builder.Append('-');
        }
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
