using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;
using MdExplorer.Search.Options;
using MdExplorer.Search.Services;
using Microsoft.Extensions.Options;

namespace MdExplorer.Search.Tests.Services;

/// <summary>
/// Der Ähnlichkeits-Builder macht aus einem regulären Ausdruck einen FTS5-Vorfilter über dessen
/// Wortteile und aus einem Suchbegriff je nach Modus eine Stamm-, Nachbarschafts- oder
/// Synonymsuche, begrenzt auf die eingestellte Zahl erweiterter Begriffe.
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
    public void BuildRegexPrefilter_WithoutAGuaranteedWordStart_ReturnsEmptyPlan()
    {
        // "foo" kann mitten im Wort stehen, "bar" hinter beliebigen Zeichen. Ein Vorfilter über
        // beide verschwiege Treffer wie "Xfoo …bar".
        SimilarityQueryBuilder sut = NewBuilder();

        Fts5QueryPlan plan = sut.BuildRegexPrefilter("foo.*bar");

        Assert.Equal(string.Empty, plan.MatchExpression);
    }

    [Fact]
    public void BuildRegexPrefilter_JoinsTheGuaranteedWordStartsWithAnd()
    {
        // Das "b" aus "\b" ist eine Wortgrenze, kein Buchstabe; beide Wörter stehen in jedem Treffer.
        SimilarityQueryBuilder sut = NewBuilder();

        Fts5QueryPlan plan = sut.BuildRegexPrefilter(@"\bAPI\d{3}\s+Main\b");

        Assert.Equal("\"API\"* AND \"Main\"*", plan.MatchExpression);
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

        // Bei nur einem Begriff ergibt NEAR keinen Sinn; es bleibt bei der einfachen Suche.
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

    [Fact]
    public void BuildSimilarity_WithAnExclusion_KeepsItExcluded()
    {
        // Ohne die gemeinsame Zerlegung wurde aus "-entwurf" ein weiteres Suchwort, und die
        // Ähnlichkeitssuche brachte genau die Dokumente, die der Nutzer ausschließen wollte.
        SimilarityQueryBuilder sut = NewBuilder();

        Fts5QueryPlan plan = sut.BuildSimilarity("bericht -entwurf", SimilarityMode.Stemmed);

        Assert.Equal("(\"bericht\"*) NOT \"entwurf\"", plan.MatchExpression);
    }

    [Fact]
    public void BuildSimilarity_WithTagAndPathFilters_KeepsBothFilters()
    {
        SimilarityQueryBuilder sut = NewBuilder();

        Fts5QueryPlan plan = sut.BuildSimilarity("berichte tag:projekt path:notizen", SimilarityMode.Stemmed);

        Assert.Equal("(\"bericht\"*) AND Tags:\"projekt\"", plan.MatchExpression);
        Assert.Equal("notizen", Assert.Single(plan.PathPrefixes));
    }

    [Fact]
    public void BuildSimilarity_WithAPhrase_KeepsThePhraseExact()
    {
        SimilarityQueryBuilder sut = NewBuilder();

        Fts5QueryPlan plan = sut.BuildSimilarity("\"rote äpfel\" birnen", SimilarityMode.Stemmed);

        Assert.Equal("(\"birn\"*) AND \"rote äpfel\"", plan.MatchExpression);
    }

    [Fact]
    public void BuildSimilarity_WithOnlyAnExclusion_ProducesNoQuery()
    {
        SimilarityQueryBuilder sut = NewBuilder();

        Fts5QueryPlan plan = sut.BuildSimilarity("-entwurf", SimilarityMode.NearStem);

        Assert.Equal(string.Empty, plan.MatchExpression);
    }

    [Fact]
    public void BuildSimilarity_WithSynonymsSharingAStem_UsesTheStemOnce()
    {
        InlineSynonymProvider provider = new();
        provider.Add("auto", ["wagen", "wages"]);
        SimilarityQueryBuilder sut = NewBuilder(provider);

        Fts5QueryPlan plan = sut.BuildSimilarity("auto", SimilarityMode.NearStemSynonyms);

        Assert.Equal(1, CountOccurrences(plan.MatchExpression, "NEAR(\"auto\"* \"wag\"*"));
        Assert.DoesNotContain("\"wag\"* \"wag\"*", plan.MatchExpression, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string part) =>
        (text.Length - text.Replace(part, string.Empty, StringComparison.Ordinal).Length) / part.Length;

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

    /// <summary>Synonyme aus einer Tabelle im Test statt aus einer Datei.</summary>
    private sealed class InlineSynonymProvider : ISynonymProvider
    {
        private readonly Dictionary<string, string[]> _map = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string key, string[] synonyms) => _map[key] = synonyms;

        public IReadOnlyList<string> GetSynonyms(string stem) =>
            _map.TryGetValue(stem, out string[]? hits) ? hits : [];
    }
}
