namespace MdExplorer.Search.Services;

/// <summary>
/// Leichtgewichtiger Suffix-Stripper für deutsche Wörter. Ersetzt keinen
/// vollwertigen Snowball-Stemmer — reduziert lediglich häufige Endungen, damit FTS5-
/// Präfix-Wildcards die wichtigsten Wortformen einsammeln.
/// </summary>
/// <remarks>
/// Bewusste Vereinfachungen:
/// — Keine Doppel-Konsonanten-Reduktion (laufens → laufen, nicht lauf).
/// — Keine Umlaut-Behandlung (FTS5-Tokenizer <c>remove_diacritics 1</c> deckt das ab).
/// — Mindestlänge 3 Zeichen pro Stamm, sonst gibt es das Wort unverändert zurück.
/// </remarks>
internal static class GermanStemmer
{
    private const int MinStemLength = 3;

    // Reihenfolge: längere Suffixe zuerst — sonst frisst „-en" das „-ungen" weg.
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

        // FTS5 normalisiert Tokens (mit unicode61 + remove_diacritics) im Index zu kleinbuchstabigen Formen.
        // Die Match-Expression muss in derselben Form vorliegen, deshalb hier bewusst InvariantLower.
#pragma warning disable CA1308 // Grund: FTS5-Index (unicode61 + remove_diacritics) erwartet invariant-lowercase Tokens.
        string lower = word.ToLowerInvariant();
#pragma warning restore CA1308
        foreach (string suffix in Suffixes)
        {
            if (lower.Length - suffix.Length < MinStemLength)
            {
                continue;
            }
            if (lower.EndsWith(suffix, StringComparison.Ordinal))
            {
                return lower[..^suffix.Length];
            }
        }
        return lower;
    }
}
