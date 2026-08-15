using System.Globalization;
using System.Text;

namespace MdExplorer.Core.Text;

/// <summary>
/// Ordnet einen Sortierschlüssel dem Buchstaben zu, unter dem er in einer
/// Sprungleiste steht.
/// </summary>
/// <remarks>
/// Der Buchstabe entsteht aus dem Schlüssel, nach dem tatsächlich sortiert wird — nicht
/// aus dem Anzeigenamen. Wird nach Pfad sortiert, aber nach Titel gesprungen, landet der
/// Sprung bei einem Eintrag, der dort gar nicht steht.
/// </remarks>
public static class AlphabetIndex
{
    /// <summary>Sammelbuchstabe für Ziffern, Zeichen und leere Schlüssel.</summary>
    public const char OtherLetter = '#';

    private const int LatinLetterCount = 'Z' - 'A' + 1;

    /// <summary>Die Buchstaben der Leiste in ihrer festen Reihenfolge.</summary>
    public static IReadOnlyList<char> Letters { get; } =
    [
        .. Enumerable.Range(0, LatinLetterCount).Select(offset => (char)('A' + offset)),
        OtherLetter,
    ];

    /// <summary>
    /// Liefert den Buchstaben, unter dem <paramref name="sortKey"/> einsortiert wird.
    /// </summary>
    /// <param name="sortKey">Der Wert, nach dem die Liste sortiert ist.</param>
    /// <returns>Ein Buchstabe von <c>A</c> bis <c>Z</c> oder <see cref="OtherLetter"/>.</returns>
    /// <remarks>
    /// Umlaute laufen auf ihren Grundbuchstaben (<c>Ä</c> auf <c>A</c>, <c>ß</c> auf
    /// <c>S</c>) — sonst stünde ein Dokument namens „Änderungen" unter einem Buchstaben,
    /// den die Leiste gar nicht führt. Alles, was kein lateinischer Buchstabe ist, sammelt
    /// sich unter <see cref="OtherLetter"/>; das betrifft Ziffern ebenso wie Schriften,
    /// die dieses Alphabet nicht verwenden.
    /// </remarks>
    public static char LetterOf(string? sortKey)
    {
        if (string.IsNullOrWhiteSpace(sortKey))
        {
            return OtherLetter;
        }

        char first = sortKey.TrimStart()[0];

        // Zerlegt „Ä" in „A" + Trema; der Grundbuchstabe steht danach vorn. „ß" hat keine
        // solche Zerlegung und wird deshalb unten eigens behandelt.
        string decomposed = first.ToString(CultureInfo.InvariantCulture).Normalize(NormalizationForm.FormD);
        char baseChar = decomposed[0];

        if (baseChar == 'ß')
        {
            return 'S';
        }

        char upper = char.ToUpperInvariant(baseChar);

        return upper is >= 'A' and <= 'Z' ? upper : OtherLetter;
    }
}
