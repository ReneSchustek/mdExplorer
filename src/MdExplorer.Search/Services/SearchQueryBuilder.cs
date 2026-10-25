using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;

namespace MdExplorer.Search.Services;

/// <summary>
/// Übersetzt User-Eingaben in eine FTS5-MATCH-Expression. Jeder Begriff wird als FTS5-Phrase
/// quotiert, Sonderzeichen werden zu Trennern, und eskapierte Anführungszeichen werden über das
/// FTS5-Doppelquoten-Schema neutralisiert. Damit kann auch ein Versuch wie <c>'; DROP TABLE ...</c>
/// keine FTS5-Sonderbedeutung erlangen. Ausdrücklich großgeschriebenes <c>AND</c>, <c>OR</c> und
/// <c>NOT</c> bleiben Operatoren; klein geschrieben sind es gewöhnliche Suchwörter.
/// Path-Filter werden nicht in MATCH eingebettet, sondern als separate Präfixfilter geliefert
/// (die <c>Path</c>-Spalte ist <c>UNINDEXED</c>).
/// RegEx-Vorfilter und Similarity-Modi sind in <see cref="SimilarityQueryBuilder"/> ausgelagert.
/// </summary>
public sealed class SearchQueryBuilder : ISearchQueryBuilder
{
    /// <summary>Name der FTS5-Spalte mit den Schlagworten.</summary>
    internal const string TagColumnName = "Tags";

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

        List<string> pathPrefixes = [];
        MatchExpressionWriter writer = new();
        bool pendingNegation = false;

        foreach (QueryToken token in QueryTokenizer.Tokenize(userInput))
        {
            pendingNegation = Apply(token, writer, pathPrefixes, pendingNegation);
        }

        return new Fts5QueryPlan(writer.ToMatchExpression(), pathPrefixes);
    }

    /// <summary>Schreibt einen Begriff so, wie er in MATCH steht; Schlagworte mit Spaltenpräfix.</summary>
    /// <param name="token">Ein Term- oder Schlagwort-Token.</param>
    /// <returns>Das MATCH-Fragment.</returns>
    internal static string TermFragment(QueryToken token) => token.Type switch
    {
        QueryTokenType.TagTerm => $"{TagColumnName}:{token.Value}",
        _ => token.Value,
    };

    /// <summary>Übernimmt einen nicht leeren Pfad-Anfang.</summary>
    /// <param name="token">Ein <c>path:</c>-Token.</param>
    /// <param name="pathPrefixes">Die gesammelten Pfad-Anfänge.</param>
    internal static void AddPathPrefix(QueryToken token, List<string> pathPrefixes)
    {
        if (!string.IsNullOrEmpty(token.Value))
        {
            pathPrefixes.Add(token.Value);
        }
    }

    // Liefert, ob ein Minus auf das nächste Token wartet. Ein Minus vor "path:" verfällt: Einen
    // ausschließenden Pfadfilter gibt es nicht.
    private static bool Apply(QueryToken token, MatchExpressionWriter writer, List<string> pathPrefixes, bool pendingNegation)
    {
        switch (token.Type)
        {
            case QueryTokenType.Negation:
                return true;
            case QueryTokenType.OperatorAnd:
            case QueryTokenType.OperatorOr:
            case QueryTokenType.OperatorNot:
                writer.AddOperator(token.Type);
                return false;
            case QueryTokenType.PathFilter:
                AddPathPrefix(token, pathPrefixes);
                return false;
            default:
                writer.AddTerm(TermFragment(token), pendingNegation);
                return false;
        }
    }
}
