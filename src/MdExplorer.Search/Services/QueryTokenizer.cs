using System.Text;

namespace MdExplorer.Search.Services;

/// <summary>Art eines Tokens aus der Nutzereingabe; nur Terme und Schlagwort-Terme landen als Text im MATCH.</summary>
internal enum QueryTokenType
{
    /// <summary>Ein Suchbegriff oder eine Wortgruppe.</summary>
    Term,

    /// <summary>Eine Einschränkung auf ein Schlagwort (<c>tag:</c>).</summary>
    TagTerm,

    /// <summary>Eine Einschränkung auf einen Pfad-Anfang (<c>path:</c>).</summary>
    PathFilter,

    /// <summary>Ein Minus am Wortanfang; es schließt den folgenden Begriff aus.</summary>
    Negation,

    /// <summary>Der Operator <c>AND</c>.</summary>
    OperatorAnd,

    /// <summary>Der Operator <c>OR</c>.</summary>
    OperatorOr,

    /// <summary>Der Operator <c>NOT</c>.</summary>
    OperatorNot,
}

/// <summary>Ein Token der Nutzereingabe.</summary>
/// <param name="Type">Art des Tokens.</param>
/// <param name="Value">Der schon als FTS5-Phrase quotierte Wert; bei Operatoren und Negation leer, bei <c>path:</c> der rohe Pfad.</param>
/// <param name="Word">Das bereinigte Wort ohne Anführungszeichen, wenn das Token ein einzelnes Wort ist; sonst leer.</param>
internal readonly record struct QueryToken(QueryTokenType Type, string Value, string Word);

/// <summary>
/// Zerlegt die Sucheingabe in Tokens. Gemeinsame Grundlage für die einfache Suche und die
/// Ähnlichkeitsmodi, damit Ausschluss, <c>tag:</c> und <c>path:</c> in beiden gleich gelten.
/// </summary>
/// <remarks>
/// Gesucht wird nach jedem Tastendruck. Eine eingefügte Textseite ergäbe sonst einen
/// MATCH-Ausdruck mit tausenden Begriffen, und jede Entprellung würde ihn neu an SQLite schicken.
/// Deshalb sind Eingabelänge und Zahl der Begriffe begrenzt.
/// </remarks>
internal static class QueryTokenizer
{
    /// <summary>Längste ausgewertete Eingabe; der Rest fällt weg. Eine Suchzeile ist kein Absatz.</summary>
    public const int MaxInputLength = 1_024;

    /// <summary>Höchstzahl Begriffe und Filter je Anfrage; weitere fallen weg.</summary>
    public const int MaxTokens = 32;

    private const string TagFilterPrefix = "tag:";
    private const string PathFilterPrefix = "path:";

    /// <summary>Zeichenanzahl eines eskapierten FTS5-Anführungszeichens (<c>""</c>).</summary>
    private const int EscapedQuoteLength = 2;

    /// <summary>Zerlegt <paramref name="input"/> in höchstens <see cref="MaxTokens"/> Tokens.</summary>
    /// <param name="input">Die Sucheingabe.</param>
    /// <returns>Die Tokens in Eingabereihenfolge.</returns>
    public static List<QueryToken> Tokenize(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        string bounded = input.Length > MaxInputLength ? input[..MaxInputLength] : input;
        List<QueryToken> tokens = [];
        int position = 0;
        while (position < bounded.Length && tokens.Count < MaxTokens)
        {
            QueryToken? token = ReadToken(bounded, ref position);
            if (token.HasValue)
            {
                tokens.Add(token.Value);
            }
        }
        return tokens;
    }

    private static QueryToken? ReadToken(string input, ref int position)
    {
        char current = input[position];
        if (char.IsWhiteSpace(current))
        {
            position++;
            return null;
        }

        if (current == '-' && IsAtStartOfWord(input, position))
        {
            position++;
            return new QueryToken(QueryTokenType.Negation, string.Empty, string.Empty);
        }

        if (current == '"')
        {
            return ReadPhrase(input, ref position);
        }

        (string word, int afterWord) = ConsumeWord(input, position);
        position = afterWord;
        if (word.Length == 0)
        {
            position++;
            return null;
        }

        return ClassifyWord(word, input, ref position);
    }

    private static QueryToken? ReadPhrase(string input, ref int position)
    {
        (string phrase, int next) = ConsumeQuoted(input, position);
        position = next;
        return phrase.Length == 0
            ? null
            : new QueryToken(QueryTokenType.Term, SearchTokens.FormatPhrase(SearchTokens.LimitPhrase(phrase), withWildcard: false), string.Empty);
    }

    private static QueryToken? ClassifyWord(string word, string input, ref int position)
    {
        if (TryStripPrefix(word, TagFilterPrefix, out string tagValue))
        {
            string formatted = FormatFilterValue(ConsumeRawFilterValue(tagValue, input, ref position));
            return formatted.Length == 0 ? null : new QueryToken(QueryTokenType.TagTerm, formatted, string.Empty);
        }

        if (TryStripPrefix(word, PathFilterPrefix, out string pathValue))
        {
            string raw = ConsumeRawFilterValue(pathValue, input, ref position);
            return new QueryToken(QueryTokenType.PathFilter, SearchTokens.LimitPhrase(raw), string.Empty);
        }

        QueryTokenType? operatorType = OperatorOf(word);
        return operatorType.HasValue
            ? new QueryToken(operatorType.Value, string.Empty, string.Empty)
            : PlainTerm(word);
    }

    private static QueryTokenType? OperatorOf(string word) => word switch
    {
        "AND" => QueryTokenType.OperatorAnd,
        "OR" => QueryTokenType.OperatorOr,
        "NOT" => QueryTokenType.OperatorNot,
        _ => null,
    };

    // Ein Wort mit Sternchen ist eine Präfixsuche und hat kein eigenes Wort für den Stemmer.
    private static QueryToken? PlainTerm(string word)
    {
        bool wildcard = word.EndsWith('*');
        string sanitized = SearchTokens.SanitizeTerm(wildcard ? word[..^1] : word);
        if (sanitized.Length == 0)
        {
            return null;
        }
        string stemmable = wildcard ? string.Empty : sanitized;
        return new QueryToken(QueryTokenType.Term, SearchTokens.FormatPhrase(sanitized, wildcard), stemmable);
    }

    private static string FormatFilterValue(string raw)
    {
        string sanitized = SearchTokens.SanitizeTerm(raw);
        return sanitized.Length == 0 ? string.Empty : SearchTokens.FormatPhrase(sanitized, withWildcard: false);
    }

    private static string ConsumeRawFilterValue(string inlineValue, string input, ref int position)
    {
        if (inlineValue.Length > 0)
        {
            return inlineValue;
        }
        if (position < input.Length && input[position] == '"')
        {
            (string phrase, int next) = ConsumeQuoted(input, position);
            position = next;
            return phrase;
        }
        (string word, int afterWord) = ConsumeWord(input, position);
        position = afterWord;
        return word;
    }

    private static bool TryStripPrefix(string word, string prefix, out string remainder)
    {
        if (word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            remainder = word[prefix.Length..];
            return true;
        }
        remainder = string.Empty;
        return false;
    }

    private static (string Value, int Next) ConsumeQuoted(string input, int startIndex)
    {
        StringBuilder builder = new();
        int index = startIndex + 1;
        while (index < input.Length)
        {
            char character = input[index];
            if (character == '"')
            {
                if (index + 1 < input.Length && input[index + 1] == '"')
                {
                    _ = builder.Append('"');
                    index += EscapedQuoteLength;
                    continue;
                }
                index++;
                return (builder.ToString(), index);
            }
            _ = builder.Append(character);
            index++;
        }
        return (builder.ToString(), index);
    }

    /// <summary>
    /// Liest ein Wort bis zum nächsten Leerzeichen oder bis zum nächsten Anführungszeichen.
    /// </summary>
    /// <remarks>
    /// Ohne das Anführungszeichen als Ende läse diese Schleife <c>tag:"zwei teile"</c> als
    /// ein Wort <c>tag:"zwei</c>; der Wert gälte damit als mitgeliefert, und der Zweig, der
    /// ein Anführungszeichen nach dem Doppelpunkt lesen soll, würde nie erreicht. Heraus käme
    /// <c>Tags:"zwei" AND "teile"</c>; die Einschränkung suchte ein Schlagwort namens
    /// <c>zwei</c> und dazu, unabhängig davon, das Wort <c>teile</c> irgendwo im Text. Bei
    /// <c>path:"mein ordner"</c> stünde am Ende ein Pfad-Anfang <c>"mein</c>, samt
    /// Anführungszeichen.
    /// </remarks>
    private static (string Value, int Next) ConsumeWord(string input, int startIndex)
    {
        int index = startIndex;
        while (index < input.Length && !char.IsWhiteSpace(input[index]) && input[index] != '"')
        {
            index++;
        }
        return (input[startIndex..index], index);
    }

    private static bool IsAtStartOfWord(string input, int position) =>
        position == 0 || char.IsWhiteSpace(input[position - 1]);
}
