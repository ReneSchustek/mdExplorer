using System.Globalization;
using System.Text;
using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;
using MdExplorer.Search.Options;
using Microsoft.Extensions.Options;

namespace MdExplorer.Search.Services;

/// <summary>
/// Implementierung von <see cref="ISimilarityQueryBuilder"/>. Baut den RegEx-Vorfilter
/// und die Similarity-Modi auf den Tokenizer-Helfern aus
/// <see cref="SearchTokens"/> auf; der Match-Build-Pfad für reine User-Eingaben
/// bleibt im <see cref="SearchQueryBuilder"/>.
/// </summary>
public sealed class SimilarityQueryBuilder : ISimilarityQueryBuilder
{
    private readonly ISearchQueryBuilder _baseBuilder;
    private readonly ISynonymProvider _synonymProvider;
    private readonly SearchOptions _options;

    /// <summary>Erzeugt den Builder und löst Synonym-Provider sowie Options auf.</summary>
    public SimilarityQueryBuilder(
        ISearchQueryBuilder baseBuilder,
        ISynonymProvider synonymProvider,
        IOptions<SearchOptions> options)
    {
        ArgumentNullException.ThrowIfNull(baseBuilder);
        ArgumentNullException.ThrowIfNull(synonymProvider);
        ArgumentNullException.ThrowIfNull(options);
        _baseBuilder = baseBuilder;
        _synonymProvider = synonymProvider;
        _options = options.Value;
    }

    /// <inheritdoc />
    public Fts5QueryPlan BuildRegexPrefilter(string regexPattern)
    {
        ArgumentNullException.ThrowIfNull(regexPattern);
        if (string.IsNullOrWhiteSpace(regexPattern))
        {
            return new Fts5QueryPlan(string.Empty, []);
        }

        // Jeder gesicherte Wortanfang steht in jedem Treffer; verbunden wird deshalb mit AND.
        List<string> phrases = [];
        foreach (string literal in RegexRequiredLiterals.Extract(regexPattern))
        {
            string sanitized = SearchTokens.SanitizeTerm(literal);
            if (sanitized.Length > 0 && phrases.Count < QueryTokenizer.MaxTokens)
            {
                phrases.Add(SearchTokens.FormatPhrase(sanitized, withWildcard: true));
            }
        }
        return new Fts5QueryPlan(string.Join(" AND ", phrases), []);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Die Eingabe geht durch dieselbe Zerlegung wie die einfache Suche. Gestemmt und per
    /// <c>NEAR</c>/<c>OR</c> verbunden werden nur einzelne, eingeschlossene Wörter; Wortgruppen und
    /// <c>tag:</c> bleiben Pflicht (<c>AND</c>), ausgeschlossene Begriffe bleiben ausgeschlossen, und
    /// <c>path:</c> bleibt ein Pfadfilter. Großgeschriebene Operatoren entfallen: Wie die Wörter
    /// zusammenhängen, bestimmt hier der Modus.
    /// </remarks>
    public Fts5QueryPlan BuildSimilarity(string? userInput, SimilarityMode mode)
    {
        if (mode == SimilarityMode.None || string.IsNullOrWhiteSpace(userInput))
        {
            return _baseBuilder.Build(userInput);
        }

        SimilarityParts parts = Split(userInput);
        string stemExpression = parts.Words.Count > 0 ? BuildStemExpression(parts.Words, mode) : string.Empty;
        if (parts.Required.Count == 0 && parts.Excluded.Count == 0)
        {
            return new Fts5QueryPlan(stemExpression, parts.PathPrefixes);
        }

        return new Fts5QueryPlan(CombineWithTerms(stemExpression, parts), parts.PathPrefixes);
    }

    // Neben weiteren Begriffen kommt der Stamm-Ausdruck in Klammern: Er enthält OR, und AND wie NOT
    // binden in FTS5 stärker.
    private static string CombineWithTerms(string stemExpression, SimilarityParts parts)
    {
        MatchExpressionWriter writer = new();
        if (stemExpression.Length > 0)
        {
            writer.AddGroup(stemExpression);
        }
        foreach (string required in parts.Required)
        {
            writer.AddTerm(required, excluded: false);
        }
        foreach (string excluded in parts.Excluded)
        {
            writer.AddTerm(excluded, excluded: true);
        }
        return writer.ToMatchExpression();
    }

    // Einzelne Wörter ab zwei Zeichen werden gestemmt. Ein Buchstabe als Präfix träfe fast jedes
    // Wort, und eine Wortgruppe hat keinen Stamm; beides bleibt ein Pflichtbegriff.
    private static SimilarityParts Split(string userInput)
    {
        SimilarityParts parts = new();
        bool pendingNegation = false;
        foreach (QueryToken token in QueryTokenizer.Tokenize(userInput))
        {
            switch (token.Type)
            {
                case QueryTokenType.Negation:
                    pendingNegation = true;
                    continue;
                case QueryTokenType.PathFilter:
                    SearchQueryBuilder.AddPathPrefix(token, parts.PathPrefixes);
                    break;
                case QueryTokenType.Term or QueryTokenType.TagTerm:
                    parts.Add(token, pendingNegation);
                    break;
                default:
                    break;
            }
            pendingNegation = false;
        }
        return parts;
    }

    private static List<string> StemAll(IEnumerable<string> terms)
    {
        List<string> stems = [];
        foreach (string term in terms)
        {
            string stem = GermanStemmer.Stem(term);
            if (stem.Length > 0)
            {
                stems.Add(stem);
            }
        }
        return stems;
    }

    private static string BuildOrMatch(IReadOnlyList<string> stems)
    {
        StringBuilder builder = new();
        for (int i = 0; i < stems.Count; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(" OR ");
            }
            _ = builder.Append(SearchTokens.FormatPhrase(stems[i], withWildcard: true));
        }
        return builder.ToString();
    }

    private string BuildStemExpression(IReadOnlyList<string> words, SimilarityMode mode)
    {
        List<string> stems = StemAll(words);
        IReadOnlyList<string> expanded = ExpandWithSynonymsIfRequested(stems, mode);
        IReadOnlyList<string> truncated = TruncateToMaxTokens(expanded);
        return mode is SimilarityMode.NearStem or SimilarityMode.NearStemSynonyms && truncated.Count > 1
            ? BuildNearMatch(truncated)
            : BuildOrMatch(truncated);
    }

    private IReadOnlyList<string> ExpandWithSynonymsIfRequested(IReadOnlyList<string> stems, SimilarityMode mode)
    {
        if (mode != SimilarityMode.NearStemSynonyms)
        {
            return stems;
        }

        // Verglichen wird am Stamm, denn der Stamm landet in der Anfrage. Zwei Synonyme mit
        // demselben Stamm belegten sonst zwei Plätze des begrenzten Budgets mit demselben Begriff.
        List<string> expanded = [.. stems];
        HashSet<string> seen = new(stems, StringComparer.OrdinalIgnoreCase);
        foreach (string stem in stems)
        {
            foreach (string synonym in _synonymProvider.GetSynonyms(stem))
            {
                string sanitized = SearchTokens.SanitizeTerm(synonym);
                string synonymStem = sanitized.Length == 0 ? string.Empty : GermanStemmer.Stem(sanitized);
                if (synonymStem.Length > 0 && seen.Add(synonymStem))
                {
                    expanded.Add(synonymStem);
                }
            }
        }
        return expanded;
    }

    private IReadOnlyList<string> TruncateToMaxTokens(IReadOnlyList<string> expanded)
    {
        // Die Stämme der Eingabe stehen vorn, die Synonyme dahinter. Wird gekürzt, fallen also
        // zuerst Synonyme weg.
        int limit = Math.Max(1, _options.MaxExpandedTokens);
        if (expanded.Count <= limit)
        {
            return expanded;
        }
        List<string> truncated = new(limit);
        for (int i = 0; i < limit; i++)
        {
            truncated.Add(expanded[i]);
        }
        return truncated;
    }

    private string BuildNearMatch(IReadOnlyList<string> stems)
    {
        int window = Math.Max(1, _options.NearProximityWindow);
        StringBuilder builder = new();
        _ = builder.Append("NEAR(");
        for (int i = 0; i < stems.Count; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(' ');
            }
            _ = builder.Append(SearchTokens.FormatPhrase(stems[i], withWildcard: true));
        }
        _ = builder.Append(", ").Append(window.ToString(CultureInfo.InvariantCulture)).Append(')');

        // Das angehängte OR hält Dokumente im Ergebnis, in denen die Wörter weiter auseinanderstehen;
        // NEAR allein schlösse sie aus.
        _ = builder.Append(" OR ");
        _ = builder.Append(BuildOrMatch(stems));
        return builder.ToString();
    }

    /// <summary>Die Teile einer Eingabe im Ähnlichkeitsmodus.</summary>
    private sealed class SimilarityParts
    {
        private const int MinStemmableLength = 2;

        public List<string> Words { get; } = [];

        public List<string> Required { get; } = [];

        public List<string> Excluded { get; } = [];

        public List<string> PathPrefixes { get; } = [];

        public void Add(QueryToken token, bool excluded)
        {
            string fragment = SearchQueryBuilder.TermFragment(token);
            if (excluded)
            {
                Excluded.Add(fragment);
            }
            else if (token.Type == QueryTokenType.Term && token.Word.Length >= MinStemmableLength && !token.Word.Contains(' ', StringComparison.Ordinal))
            {
                Words.Add(token.Word);
            }
            else
            {
                Required.Add(fragment);
            }
        }
    }
}
