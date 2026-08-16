using System.Text;
using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;

namespace MdExplorer.Search.Services;

/// <summary>
/// Übersetzt User-Eingaben in eine FTS5-MATCH-Expression. Das Verfahren ist konservativ:
/// Bare-Words werden grundsätzlich als FTS5-Strings quotiert (auch Operator-Reservierungen
/// wie <c>AND</c>/<c>OR</c>/<c>NOT</c>/<c>NEAR</c> verlieren so ihre Bedeutung), Sonderzeichen
/// werden gefiltert. Damit kann auch ein Versuch wie <c>'; DROP TABLE ...</c> keine FTS5-Sonderbedeutung
/// erlangen — eskapierte Anführungszeichen werden über das FTS5-Doppelquoten-Schema neutralisiert.
/// Path-Filter werden nicht in MATCH eingebettet, sondern als separate Präfixfilter geliefert
/// (die <c>Path</c>-Spalte ist <c>UNINDEXED</c>).
/// RegEx-Vorfilter und Similarity-Modi sind in <see cref="SimilarityQueryBuilder"/> ausgelagert.
/// </summary>
public sealed class SearchQueryBuilder : ISearchQueryBuilder
{
    private const string TagColumnName = "Tags";
    private const string TagFilterPrefix = "tag:";
    private const string PathFilterPrefix = "path:";

    /// <summary>Zeichenanzahl eines eskapierten FTS5-Anführungszeichens (<c>""</c>).</summary>
    private const int EscapedQuoteLength = 2;

    /// <summary>Erzeugt den Builder.</summary>
    public SearchQueryBuilder()
    {
    }

    /// <inheritdoc />
    public Fts5QueryPlan Build(string? userInput)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            return new Fts5QueryPlan(string.Empty, []);
        }

        List<Token> tokens = Tokenize(userInput);
        List<string> pathPrefixes = [];
        StringBuilder matchBuilder = new();
        bool pendingNegation = false;
        TokenType? lastEmitted = null;

        foreach (Token token in tokens)
        {
            switch (token.Type)
            {
                case TokenType.Negation:
                    pendingNegation = true;
                    break;
                case TokenType.OperatorAnd:
                case TokenType.OperatorOr:
                case TokenType.OperatorNot:
                    EmitOperator(matchBuilder, token.Type, ref lastEmitted, ref pendingNegation);
                    break;
                case TokenType.PathFilter:
                    AddPathPrefix(token, pathPrefixes, ref pendingNegation);
                    break;
                default:
                    EmitTermToken(token, matchBuilder, ref pendingNegation, ref lastEmitted);
                    break;
            }
        }

        return new Fts5QueryPlan(matchBuilder.ToString().Trim(), pathPrefixes);
    }

    private static void AddPathPrefix(Token token, List<string> pathPrefixes, ref bool pendingNegation)
    {
        if (!string.IsNullOrEmpty(token.Value))
        {
            pathPrefixes.Add(token.Value);
        }
        pendingNegation = false;
    }

    private static void EmitTermToken(Token token, StringBuilder matchBuilder, ref bool pendingNegation, ref TokenType? lastEmitted)
    {
        string fragment = token.Type switch
        {
            TokenType.TagTerm => $"{TagColumnName}:{token.Value}",
            _ => token.Value,
        };

        if (string.IsNullOrEmpty(fragment))
        {
            pendingNegation = false;
            return;
        }

        EmitTerm(matchBuilder, fragment, pendingNegation, ref lastEmitted);
        pendingNegation = false;
    }

    private static void EmitOperator(StringBuilder builder, TokenType type, ref TokenType? lastEmitted, ref bool pendingNegation)
    {
        if (lastEmitted is null or TokenType.OperatorAnd or TokenType.OperatorOr or TokenType.OperatorNot)
        {
            return;
        }

        string keyword = type switch
        {
            TokenType.OperatorOr => "OR",
            TokenType.OperatorNot => "NOT",
            _ => "AND",
        };
        _ = builder.Append(' ').Append(keyword).Append(' ');
        lastEmitted = type;
        pendingNegation = false;
    }

    private static void EmitTerm(StringBuilder builder, string fragment, bool negated, ref TokenType? lastEmitted)
    {
        bool needsImplicitAnd = lastEmitted is TokenType.Term or TokenType.TagTerm;
        if (needsImplicitAnd)
        {
            _ = builder.Append(' ');
            _ = builder.Append(negated ? "NOT" : "AND");
            _ = builder.Append(' ');
        }
        else if (negated)
        {
            if (builder.Length > 0)
            {
                _ = builder.Append(' ');
            }
            _ = builder.Append("NOT ");
        }
        else if (builder.Length > 0 && builder[builder.Length - 1] != ' ')
        {
            _ = builder.Append(' ');
        }

        _ = builder.Append(fragment);
        lastEmitted = TokenType.Term;
    }

    private static List<Token> Tokenize(string input)
    {
        List<Token> tokens = [];
        int position = 0;
        while (position < input.Length)
        {
            char current = input[position];
            if (char.IsWhiteSpace(current))
            {
                position++;
                continue;
            }

            if (current == '-' && IsAtStartOfWord(input, position))
            {
                tokens.Add(new Token(TokenType.Negation, string.Empty));
                position++;
                continue;
            }

            if (current == '"')
            {
                (string phrase, int next) = ConsumeQuoted(input, position);
                position = next;
                if (phrase.Length > 0)
                {
                    tokens.Add(new Token(TokenType.Term, SearchTokens.FormatPhrase(phrase, withWildcard: false)));
                }
                continue;
            }

            (string word, int afterWord) = ConsumeWord(input, position);
            position = afterWord;
            if (word.Length == 0)
            {
                position++;
                continue;
            }

            Token? produced = ClassifyWord(word, input, ref position);
            if (produced.HasValue)
            {
                tokens.Add(produced.Value);
            }
        }
        return tokens;
    }

    private static Token? ClassifyWord(string word, string input, ref int position)
    {
        if (TryStripPrefix(word, TagFilterPrefix, out string tagValue))
        {
            string formatted = ConsumeFilterValue(tagValue, input, ref position, withWildcard: false);
            return string.IsNullOrEmpty(formatted) ? null : new Token(TokenType.TagTerm, formatted);
        }

        if (TryStripPrefix(word, PathFilterPrefix, out string pathValue))
        {
            string raw = ConsumeRawFilterValue(pathValue, input, ref position);
            return new Token(TokenType.PathFilter, raw);
        }

        if (string.Equals(word, "AND", StringComparison.Ordinal))
        {
            return new Token(TokenType.OperatorAnd, string.Empty);
        }
        if (string.Equals(word, "OR", StringComparison.Ordinal))
        {
            return new Token(TokenType.OperatorOr, string.Empty);
        }
        if (string.Equals(word, "NOT", StringComparison.Ordinal))
        {
            return new Token(TokenType.OperatorNot, string.Empty);
        }

        bool wildcard = word.EndsWith('*');
        string body = wildcard ? word[..^1] : word;
        string sanitized = SearchTokens.SanitizeTerm(body);
        if (sanitized.Length == 0)
        {
            return null;
        }

        return new Token(TokenType.Term, SearchTokens.FormatPhrase(sanitized, wildcard));
    }

    private static string ConsumeFilterValue(string inlineValue, string input, ref int position, bool withWildcard)
    {
        string raw = ConsumeRawFilterValue(inlineValue, input, ref position);
        if (raw.Length == 0)
        {
            return string.Empty;
        }
        bool wildcard = withWildcard && raw.EndsWith('*');
        string body = wildcard ? raw[..^1] : raw;
        string sanitized = SearchTokens.SanitizeTerm(body);
        return sanitized.Length == 0 ? string.Empty : SearchTokens.FormatPhrase(sanitized, wildcard);
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
    /// Liest ein Wort bis zum nächsten Leerzeichen — oder bis zum nächsten Anführungszeichen.
    /// </summary>
    /// <remarks>
    /// Das Anführungszeichen als Ende kam am 16.08.2026 dazu. Vorher las diese Schleife
    /// <c>tag:"zwei woerter"</c> als **ein** Wort <c>tag:"zwei</c>; der Wert galt damit als
    /// mitgeliefert, und der Zweig, der ein Anführungszeichen nach dem Doppelpunkt lesen
    /// sollte, wurde nie erreicht. Heraus kam <c>Tags:"zwei" AND "woerter"</c> — die
    /// Einschränkung suchte ein Schlagwort namens <c>zwei</c> und dazu, unabhängig davon, das
    /// Wort <c>woerter</c> irgendwo im Text. Bei <c>path:"mein ordner"</c> stand am Ende ein
    /// Pfad-Anfang <c>"mein</c>, samt Anführungszeichen.
    /// </remarks>
    private static (string Value, int Next) ConsumeWord(string input, int startIndex)
    {
        StringBuilder builder = new();
        int index = startIndex;
        while (index < input.Length)
        {
            char character = input[index];
            if (char.IsWhiteSpace(character))
            {
                break;
            }

            // Ein Anführungszeichen beginnt immer eine Wortgruppe — hier endet das Wort davor.
            // Am Zeilenanfang übernimmt das der Aufrufer; nach einem Doppelpunkt diese Stelle.
            if (character == '"')
            {
                break;
            }
            _ = builder.Append(character);
            index++;
        }
        return (builder.ToString(), index);
    }

    private static bool IsAtStartOfWord(string input, int position)
    {
        if (position == 0)
        {
            return true;
        }
        char previous = input[position - 1];
        return char.IsWhiteSpace(previous);
    }

    private enum TokenType
    {
        Term,
        TagTerm,
        PathFilter,
        Negation,
        OperatorAnd,
        OperatorOr,
        OperatorNot,
    }

    private readonly record struct Token(TokenType Type, string Value);
}
