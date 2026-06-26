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
/// <see cref="SearchTokens"/> auf — der Match-Build-Pfad fuer reine User-Eingaben
/// bleibt im <see cref="SearchQueryBuilder"/>.
/// </summary>
public sealed class SimilarityQueryBuilder : ISimilarityQueryBuilder
{
    private readonly ISearchQueryBuilder _baseBuilder;
    private readonly ISynonymProvider _synonymProvider;
    private readonly SearchOptions _options;

    /// <summary>Erzeugt den Builder und loest Synonym-Provider sowie Options auf.</summary>
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

        List<string> tokens = SearchTokens.ExtractWordTokens(regexPattern);
        if (tokens.Count == 0)
        {
            return new Fts5QueryPlan(string.Empty, []);
        }

        StringBuilder builder = new();
        bool first = true;
        foreach (string token in tokens)
        {
            if (!first)
            {
                _ = builder.Append(" OR ");
            }
            _ = builder.Append(SearchTokens.FormatPhrase(token, withWildcard: true));
            first = false;
        }
        return new Fts5QueryPlan(builder.ToString(), []);
    }

    /// <inheritdoc />
    public Fts5QueryPlan BuildSimilarity(string? userInput, SimilarityMode mode)
    {
        if (mode == SimilarityMode.None || string.IsNullOrWhiteSpace(userInput))
        {
            return _baseBuilder.Build(userInput);
        }

        List<string> rawTerms = SearchTokens.ExtractWordTokens(userInput);
        if (rawTerms.Count == 0)
        {
            return new Fts5QueryPlan(string.Empty, []);
        }

        List<string> stems = StemAll(rawTerms);
        IReadOnlyList<string> expanded = ExpandWithSynonymsIfRequested(stems, mode);
        IReadOnlyList<string> truncated = TruncateToMaxTokens(expanded);

        string matchExpression = mode is SimilarityMode.NearStem or SimilarityMode.NearStemSynonyms && truncated.Count > 1
            ? BuildNearMatch(truncated)
            : BuildOrMatch(truncated);

        return new Fts5QueryPlan(matchExpression, []);
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

    private IReadOnlyList<string> ExpandWithSynonymsIfRequested(IReadOnlyList<string> stems, SimilarityMode mode)
    {
        if (mode != SimilarityMode.NearStemSynonyms)
        {
            return stems;
        }

        List<string> expanded = [.. stems];
        HashSet<string> seen = new(stems, StringComparer.OrdinalIgnoreCase);
        foreach (string stem in stems)
        {
            foreach (string synonym in _synonymProvider.GetSynonyms(stem))
            {
                string sanitized = SearchTokens.SanitizeTerm(synonym);
                if (sanitized.Length > 0 && seen.Add(sanitized))
                {
                    expanded.Add(GermanStemmer.Stem(sanitized));
                }
            }
        }
        return expanded;
    }

    private IReadOnlyList<string> TruncateToMaxTokens(IReadOnlyList<string> expanded)
    {
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
        _ = builder.Append(" OR ");
        _ = builder.Append(BuildOrMatch(stems));
        return builder.ToString();
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
}
