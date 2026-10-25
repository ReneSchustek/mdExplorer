namespace MdExplorer.Search.Services;

/// <summary>
/// Leichtgewichtiger Suffix-Stripper für deutsche Wörter. Ersetzt keinen
/// vollwertigen Snowball-Stemmer und reduziert lediglich häufige Endungen, damit FTS5-
/// Präfix-Wildcards die wichtigsten Wortformen einsammeln.
/// </summary>
/// <remarks>
/// Vereinfachungen:
/// Je Wort fällt höchstens eine Endung weg (Bedeutungen → Bedeut, aber Läufers → Läufer).
/// Keine Umlaut-Behandlung, das übernimmt der FTS5-Tokenizer mit <c>remove_diacritics 2</c>.
/// Keine Umwandlung der Groß- und Kleinschreibung: Der Tokenizer faltet sie in Index und Anfrage
/// selbst, der Stamm behält deshalb die Schreibweise der Eingabe.
/// Mindestlänge 3 Zeichen pro Stamm, sonst gibt es das Wort unverändert zurück.
/// </remarks>
internal static class GermanStemmer
{
    // Der Stamm geht als Präfix-Wildcard in die Anfrage. Ein Stamm aus zwei Buchstaben träfe dort
    // einen Großteil des Wortschatzes.
    private const int MinStemLength = 3;

    // Reihenfolge: längere Suffixe zuerst, sonst frisst „-en" das „-ungen" weg.
    private static readonly string[] Suffixes =
    [
        "ungen",
        "lich",
        "isch",
        "keit",
        "heit",
        "bar",
        "ung",
        "end",
        "ern",
        "ten",
        "tem",
        "ter",
        "tes",
        "er",
        "en",
        "es",
        "em",
        "et",
        "st",
        "e",
        "n",
        "s",
    ];

    /// <summary>
    /// Reduziert <paramref name="word"/> auf einen FTS5-tauglichen Stamm. Gibt das
    /// Ursprungs-Wort zurück, wenn keine sinnvolle Reduktion möglich ist.
    /// </summary>
    public static string Stem(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (word.Length <= MinStemLength)
        {
            return word;
        }

        foreach (string suffix in Suffixes)
        {
            if (word.Length - suffix.Length < MinStemLength)
            {
                continue;
            }
            if (word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return word[..^suffix.Length];
            }
        }
        return word;
    }
}
