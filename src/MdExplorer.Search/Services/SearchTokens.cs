using System.Globalization;
using System.Text;

namespace MdExplorer.Search.Services;

/// <summary>
/// Gemeinsame, seiteneffektfreie Tokenizer-Helfer für <see cref="SearchQueryBuilder"/>
/// und <see cref="SimilarityQueryBuilder"/>.
/// </summary>
internal static class SearchTokens
{
    // Kappt überlange Eingaben, bevor sie in die MATCH-Expression gehen. Der Wert ist gesetzt, nicht gemessen.
    private const int MaxTermLength = 128;

    // Eine Wortgruppe darf länger sein als ein Wort, aber keine eingefügte Seite.
    private const int MaxPhraseLength = 256;

    /// <summary>Kürzt eine Wortgruppe oder einen Pfad auf eine feste Höchstlänge.</summary>
    /// <param name="raw">Die Wortgruppe.</param>
    /// <returns>Höchstens 256 Zeichen.</returns>
    public static string LimitPhrase(string raw) =>
        raw.Length > MaxPhraseLength ? raw[..MaxPhraseLength] : raw;

    /// <summary>Quotiert als FTS5-Phrase, eskapiert <c>"</c> per Verdopplung, hängt optional <c>*</c> an.</summary>
    public static string FormatPhrase(string content, bool withWildcard)
    {
        StringBuilder builder = new("\"", content.Length + 4);
        foreach (char character in content)
        {
            if (character == '"')
            {
                _ = builder.Append("\"\"");
                continue;
            }
            _ = builder.Append(character);
        }
        _ = builder.Append('"');
        if (withWildcard)
        {
            _ = builder.Append('*');
        }
        return builder.ToString();
    }

    /// <summary>
    /// Macht aus Nicht-Wort-Zeichen Trenner, verwirft Anführungszeichen, begrenzt auf
    /// <c>MaxTermLength</c>.
    /// </summary>
    /// <remarks>
    /// Der Tokenizer des Index (<c>unicode61</c>) zerlegt „E-Mail“ an jedem Satzzeichen in
    /// <c>e</c> und <c>mail</c>. Fiele das Satzzeichen ersatzlos weg, suchte die Anfrage
    /// <c>email</c> und fände nichts, ebenso jeder Klick auf ein Schlagwort mit Bindestrich. Als
    /// Leerzeichen bleibt es eine Phrase aus denselben Teilen, und FTS5 verlangt sie in genau
    /// dieser Folge.
    /// </remarks>
    public static string SanitizeTerm(string raw)
    {
        if (raw.Length == 0)
        {
            return string.Empty;
        }
        int limit = Math.Min(raw.Length, MaxTermLength);
        StringBuilder builder = new(limit);
        for (int i = 0; i < limit; i++)
        {
            char character = raw[i];
            if (IsTermCharacter(character))
            {
                _ = builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != ' ')
            {
                _ = builder.Append(' ');
            }
        }
        return builder.ToString().TrimEnd();
    }

    private static bool IsTermCharacter(char character) =>
        character != '"' && CharUnicodeInfo.GetUnicodeCategory(character)
            is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.ModifierLetter or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.ConnectorPunctuation;
}
