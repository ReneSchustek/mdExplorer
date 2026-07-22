using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;
using MdExplorer.Search.Options;
using MdExplorer.Search.Services;
using Microsoft.Extensions.Options;

namespace MdExplorer.Search.Tests.Services;

/// <summary>
/// Tests für die Builder-Pfade des RegEx-Vorfilters und der Similarity-Modi.
/// </summary>
public sealed class SimilarityQueryBuilderTests
{
    [Fact]
    public void BuildRegexPrefilter_OnEmptyOrWhitespace_ReturnsEmptyPlan()
    {
        SimilarityQueryBuilder sut = NewBuilder();

        Fts5QueryPlan plan = sut.BuildRegexPrefilter("   ");

        Assert.Equal(string.Empty, plan.MatchExpression);
        Assert.Empty(plan.PathPrefixes);
    }

    [Fact]
    public void BuildRegexPrefilter_OnPatternWithoutWordTokens_ReturnsEmptyPlan()
    {
        SimilarityQueryBuilder sut = NewBuilder();

        Fts5QueryPlan plan = sut.BuildRegexPrefilter(".+?");

        Assert.Equal(string.Empty, plan.MatchExpression);
    }

    [Fact]
    public void BuildRegexPrefilter_ExtractsWordTokensAsOrWithPrefixWildcards()
    {
        SimilarityQueryBuilder sut = NewBuilder();

        Fts5QueryPlan plan = sut.BuildRegexPrefilter("foo.*bar");

        Assert.Equal("\"foo\"* OR \"bar\"*", plan.MatchExpression);
    }

    [Fact]
    public void BuildRegexPrefilter_PicksUpMultiCharWordRuns()
    {
        SimilarityQueryBuilder sut = NewBuilder();

        // \bAPI\d{3}\s+Main\b — Word-Runs ≥ 2: "bAPI" (Backslash zählt nicht, b-A-P-I bilden den Run) und "Main".
        Fts5QueryPlan plan = sut.BuildRegexPrefilter(@"\bAPI\d{3}\s+Main\b");

        Assert.Equal("\"bAPI\"* OR \"Main\"*", plan.MatchExpression);
    }

    [Fact]
    public void BuildSimilarity_OnModeNone_DelegatesToBuild()
    {
        SearchQueryBuilder baseBuilder = new();
        SimilarityQueryBuilder sut = NewBuilder(baseBuilder: baseBuilder);

        Fts5QueryPlan plan = sut.BuildSimilarity("apfel", SimilarityMode.None);

        Assert.Equal(baseBuilder.Build("apfel").MatchExpression, plan.MatchExpression);
    }

    [Fact]
    public void BuildSimilarity_OnStemmedMode_ProducesPrefixWildcardOverStem()
    {
        SimilarityQueryBuilder sut = NewBuilder();

        // "laufen" → Stamm "lauf" (Endung "-en" entfernt).
        Fts5QueryPlan plan = sut.BuildSimilarity("laufen", SimilarityMode.Stemmed);

        Assert.Equal("\"lauf\"*", plan.MatchExpression);
    }

    [Fact]
    public void BuildSimilarity_OnNearStemMode_ProducesNearOperatorPlusOrFallback()
    {
        SimilarityQueryBuilder sut = NewBuilder();

        // "roter apfel" → Stämme "rot" + "apfel" → NEAR + OR-Fallback.
        Fts5QueryPlan plan = sut.BuildSimilarity("roter apfel", SimilarityMode.NearStem);

        Assert.Equal(
            "NEAR(\"rot\"* \"apfel\"*, 5) OR \"rot\"* OR \"apfel\"*",
            plan.MatchExpression);
    }

    [Fact]
    public void BuildSimilarity_OnSingleTerm_NearStemFallsBackToOr()
    {
        SimilarityQueryBuilder sut = NewBuilder();

        Fts5QueryPlan plan = sut.BuildSimilarity("apfel", SimilarityMode.NearStem);

        // Bei nur einem Term ist NEAR nicht definiert — wir produzieren reines OR-Match.
        Assert.Equal("\"apfel\"*", plan.MatchExpression);
    }

    [Fact]
    public void BuildSimilarity_OnNearStemSynonymsMode_AddsStemmedSynonyms()
    {
        InlineSynonymProvider provider = new();
        provider.Add("auto", ["wagen", "fahrzeug"]);
        SimilarityQueryBuilder sut = NewBuilder(provider);

        Fts5QueryPlan plan = sut.BuildSimilarity("auto", SimilarityMode.NearStemSynonyms);

        // "wagen" wird gestemmt zu "wag", "fahrzeug" bleibt unverändert.
        Assert.Contains("\"auto\"*", plan.MatchExpression, StringComparison.Ordinal);
        Assert.Contains("\"wag\"*", plan.MatchExpression, StringComparison.Ordinal);
        Assert.Contains("\"fahrzeug\"*", plan.MatchExpression, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSimilarity_OnTooManyExpansions_TruncatesToMaxExpandedTokens()
    {
        InlineSynonymProvider provider = new();
        provider.Add("auto", ["aaa", "bbb", "ccc", "ddd", "eee", "fff", "ggg"]);
        SearchOptions options = new() { MaxExpandedTokens = 3 };
        SimilarityQueryBuilder sut = NewBuilder(provider, options);

        Fts5QueryPlan plan = sut.BuildSimilarity("auto", SimilarityMode.NearStemSynonyms);

        // 3 Tokens → NEAR(3 Stämme) + 3 OR-Fallback = 6 quotierte Vorkommen = 12 Doppelquotes.
        int quoteCount = plan.MatchExpression.Count(c => c == '"');
        Assert.Equal(12, quoteCount);
    }

    private static SimilarityQueryBuilder NewBuilder(
        ISynonymProvider? provider = null,
        SearchOptions? options = null,
        ISearchQueryBuilder? baseBuilder = null)
    {
        ISynonymProvider effectiveProvider = provider ?? new InlineSynonymProvider();
        IOptions<SearchOptions> wrappedOptions = Microsoft.Extensions.Options.Options.Create(options ?? new SearchOptions());
        ISearchQueryBuilder effectiveBase = baseBuilder ?? new SearchQueryBuilder();
        return new SimilarityQueryBuilder(effectiveBase, effectiveProvider, wrappedOptions);
    }

    private sealed class InlineSynonymProvider : ISynonymProvider
    {
        private readonly Dictionary<string, string[]> _map = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string key, string[] synonyms) => _map[key] = synonyms;

        public IReadOnlyList<string> GetSynonyms(string lemma) =>
            _map.TryGetValue(lemma, out string[]? hits) ? hits : [];
    }
}
