using MdExplorer.Search.Models;
using MdExplorer.Search.Services;

namespace MdExplorer.Search.Tests.Services;

/// <summary>
/// <see cref="SearchQueryBuilder"/> übersetzt die Eingabe ins Suchfeld in einen FTS5-Ausdruck: Begriffe
/// in Anführungszeichen, Operatoren übersetzt, <c>tag:</c> als Spaltensuche und <c>path:</c> als
/// eigener Filter. Ein ungeschützt durchgereichtes Sonderzeichen machte die Abfrage ungültig oder
/// änderte ihre Bedeutung.
/// </summary>
public sealed class SearchQueryBuilderTests
{
    private readonly SearchQueryBuilder _sut = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Build_OnEmptyInput_ReturnsEmptyPlan(string? input)
    {
        Fts5QueryPlan plan = _sut.Build(input);

        Assert.Equal(string.Empty, plan.MatchExpression);
        Assert.Empty(plan.PathPrefixes);
    }

    [Fact]
    public void Build_OnSimpleWord_ProducesQuotedTerm()
    {
        Fts5QueryPlan plan = _sut.Build("markdown");

        Assert.Equal("\"markdown\"", plan.MatchExpression);
        Assert.Empty(plan.PathPrefixes);
    }

    [Fact]
    public void Build_OnPhrase_PreservesQuotedToken()
    {
        Fts5QueryPlan plan = _sut.Build("\"foo bar\"");

        Assert.Equal("\"foo bar\"", plan.MatchExpression);
    }

    [Fact]
    public void Build_OnTagFilter_TranslatesToColumnMatch()
    {
        Fts5QueryPlan plan = _sut.Build("tag:projekt");

        Assert.Equal("Tags:\"projekt\"", plan.MatchExpression);
        Assert.Empty(plan.PathPrefixes);
    }

    [Fact]
    public void Build_OnPathFilter_ExtractsPrefixOutOfMatch()
    {
        Fts5QueryPlan plan = _sut.Build("path:notes/");

        Assert.Equal(string.Empty, plan.MatchExpression);
        _ = Assert.Single(plan.PathPrefixes);
        Assert.Equal("notes/", plan.PathPrefixes[0]);
    }

    [Fact]
    public void Build_OnNegationBehindATerm_EmitsNotOperator()
    {
        Fts5QueryPlan plan = _sut.Build("notiz -draft");

        Assert.Equal("\"notiz\" NOT \"draft\"", plan.MatchExpression);
    }

    [Fact]
    public void Build_OnTagAndNegation_CombinesBoth()
    {
        Fts5QueryPlan plan = _sut.Build("tag:projekt -draft");

        Assert.Equal("Tags:\"projekt\" NOT \"draft\"", plan.MatchExpression);
    }

    [Fact]
    public void Build_OnWildcardWord_AppendsPrefixWildcard()
    {
        Fts5QueryPlan plan = _sut.Build("markdo*");

        Assert.Equal("\"markdo\"*", plan.MatchExpression);
    }

    [Fact]
    public void Build_OnImplicitAnd_InsertsAndOperator()
    {
        Fts5QueryPlan plan = _sut.Build("alpha beta");

        Assert.Equal("\"alpha\" AND \"beta\"", plan.MatchExpression);
    }

    [Fact]
    public void Build_OnExplicitOr_PreservesOperator()
    {
        Fts5QueryPlan plan = _sut.Build("alpha OR beta");

        Assert.Equal("\"alpha\" OR \"beta\"", plan.MatchExpression);
    }

    [Fact]
    public void Build_OnExplicitAnd_PreservesOperator()
    {
        Fts5QueryPlan plan = _sut.Build("alpha AND beta");

        Assert.Equal("\"alpha\" AND \"beta\"", plan.MatchExpression);
    }

    [Fact]
    public void Build_OnSqlInjection_NeutralizesMetacharacters()
    {
        // Reserve-Wörter (DROP, TABLE, ...) sind in FTS5 unkritisch, sobald sie gequotet vorliegen;
        // sie verlieren ihre Operator-Bedeutung. SQL-Sonderzeichen (Semikolon, Kommentar-Marker)
        // dürfen jedoch nicht ungeschützt durchreichen.
        Fts5QueryPlan plan = _sut.Build("'; DROP TABLE MarkdownSearchIndex --");

        Assert.DoesNotContain(";", plan.MatchExpression, StringComparison.Ordinal);
        Assert.DoesNotContain("--", plan.MatchExpression, StringComparison.Ordinal);
        Assert.DoesNotContain("'", plan.MatchExpression, StringComparison.Ordinal);
        // Reserve-Wörter müssen gequotet sein (FTS5-String), nicht als Bareword-Operatoren.
        Assert.Contains("\"DROP\"", plan.MatchExpression, StringComparison.Ordinal);
        Assert.Contains("\"TABLE\"", plan.MatchExpression, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OnEmbeddedQuoteInPhrase_DoublesQuote()
    {
        Fts5QueryPlan plan = _sut.Build("\"foo\"\"bar\"");

        Assert.Equal("\"foo\"\"bar\"", plan.MatchExpression);
    }

    [Fact]
    public void Build_OnReservedKeywordLowercase_QuotesAsTerm()
    {
        Fts5QueryPlan plan = _sut.Build("and");

        Assert.Equal("\"and\"", plan.MatchExpression);
    }

    [Fact]
    public void Build_OnUmlauts_PreservesCharacters()
    {
        Fts5QueryPlan plan = _sut.Build("München");

        Assert.Equal("\"München\"", plan.MatchExpression);
    }

    [Fact]
    public void Build_OnCombinedFiltersAndTerms_BuildsCompletePlan()
    {
        Fts5QueryPlan plan = _sut.Build("markdown tag:projekt path:notes/ -draft");

        Assert.Equal("\"markdown\" AND Tags:\"projekt\" NOT \"draft\"", plan.MatchExpression);
        _ = Assert.Single(plan.PathPrefixes);
        Assert.Equal("notes/", plan.PathPrefixes[0]);
    }

    [Fact]
    public void Build_OnOnlySpecialCharacters_ReturnsEmptyMatch()
    {
        Fts5QueryPlan plan = _sut.Build("!@#$%^&*()");

        Assert.Equal(string.Empty, plan.MatchExpression);
    }

    [Fact]
    public void Build_OnPhraseWithSpecialChars_KeepsThePhraseUnchanged()
    {
        // Phrasen bleiben unverändert, der FTS5-Tokenizer zerlegt sie ohnehin an Satzzeichen.
        // Ein Anführungszeichen darin wird verdoppelt und kann die Phrase nicht beenden.
        Fts5QueryPlan plan = _sut.Build("\"foo;bar\"");

        Assert.Equal("\"foo;bar\"", plan.MatchExpression);
    }
}
