using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MdExplorer.Search.Services;

/// <summary>
/// Gemeinsame, seiteneffektfreie Tokenizer-Helfer fuer <see cref="SearchQueryBuilder"/>
/// und <see cref="SimilarityQueryBuilder"/>.
/// </summary>
internal static partial class SearchTokens
{
    private const int MaxTermLength = 128;

    [GeneratedRegex(@"\w{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex WordTokenRegex();

    /// <summary>Quotiert als FTS5-Phrase, eskapiert <c>"</c> per Verdopplung, haengt optional <c>*</c> an.</summary>
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

    /// <summary>Filtert Nicht-Wort-Zeichen, verwirft Anfuehrungszeichen, begrenzt auf <c>MaxTermLength</c>.</summary>
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
        }
        return builder.ToString();
    }

    /// <summary>Extrahiert <c>\w{2,}</c>-Treffer aus <paramref name="source"/>, sanitisiert sie und liefert die Reihenfolge.</summary>
    public static List<string> ExtractWordTokens(string source)
    {
        List<string> tokens = [];
        foreach (Match match in WordTokenRegex().Matches(source))
        {
            string sanitized = SanitizeTerm(match.Value);
            if (sanitized.Length > 0)
            {
                tokens.Add(sanitized);
            }
        }
        return tokens;
    }

    private static bool IsTermCharacter(char character) =>
        character != '"' && CharUnicodeInfo.GetUnicodeCategory(character)
            is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.ModifierLetter or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.ConnectorPunctuation;
}
