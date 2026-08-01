using MdExplorer.Search.Models;
using MdExplorer.Search.Services;

namespace MdExplorer.Search.Tests.Services;

/// <summary>
/// Prüft die Randfälle der Eingabe-Übersetzung. Der Übersetzer muss auch halbfertige
/// Eingaben verkraften — der Nutzer tippt, und nach jedem Zeichen läuft eine Suche.
/// Eine Eingabe wie <c>tag:</c> oder ein offenes Anführungszeichen ist deshalb der
/// Normalfall und darf keine ungültige FTS5-Anfrage erzeugen.
/// </summary>
public sealed class SearchQueryBuilderEdgeCaseTests
{
    [Fact]
    public void Build_WithALeadingOperator_DropsIt()
    {
        // "AND bericht" entsteht beim Löschen des ersten Begriffs. FTS5 würde die
        // führende Verknüpfung als Syntaxfehler abweisen.
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("AND bericht");

        Assert.Equal("\"bericht\"", plan.MatchExpression, StringComparer.Ordinal);
    }

    [Fact]
    public void Build_WithTwoOperatorsInARow_KeepsOnlyTheFirst()
    {
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("bericht AND OR notiz");

        Assert.Equal("\"bericht\" AND \"notiz\"", plan.MatchExpression, StringComparer.Ordinal);
    }

    [Fact]
    public void Build_WithAnExplicitNotOperator_EmitsIt()
    {
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("bericht NOT entwurf");

        Assert.Equal("\"bericht\" NOT \"entwurf\"", plan.MatchExpression, StringComparer.Ordinal);
    }

    [Fact]
    public void Build_WithANegatedTermAfterAnOperator_EmitsTheNegation()
    {
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("bericht OR -entwurf");

        Assert.Contains("NOT", plan.MatchExpression, StringComparison.Ordinal);
        Assert.Contains("\"entwurf\"", plan.MatchExpression, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithADanglingTagPrefix_IgnoresIt()
    {
        // Zwischenzustand beim Tippen von "tag:projekt".
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("bericht tag:");

        Assert.Equal("\"bericht\"", plan.MatchExpression, StringComparer.Ordinal);
    }

    [Fact]
    public void Build_WithASpaceAfterTheTagPrefix_TreatsTheNextWordAsAPlainTerm()
    {
        // Der Wert muss direkt am Doppelpunkt hängen. Steht ein Leerzeichen dazwischen,
        // gilt der Rest als gewöhnlicher Suchbegriff — das ist bewusst so, weil sonst ein
        // versehentliches Leerzeichen die Trefferliste unbemerkt auf einen Tag einengen würde.
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("tag: projekt");

        Assert.Equal("\"projekt\"", plan.MatchExpression, StringComparer.Ordinal);
    }

    [Fact]
    public void Build_WithASpaceAfterThePathPrefix_AddsNoFilter()
    {
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("path: notizen");

        Assert.Empty(plan.PathPrefixes);
    }

    [Fact]
    public void Build_WithATagValueDirectlyAttached_EmitsTheTagColumn()
    {
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("tag:projekt");

        Assert.Equal("Tags:\"projekt\"", plan.MatchExpression, StringComparer.Ordinal);
    }

    [Fact]
    public void Build_WithADanglingPathPrefix_AddsNoFilter()
    {
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("bericht path:");

        Assert.Empty(plan.PathPrefixes);
    }

    [Fact]
    public void Build_WithAnUnterminatedQuote_UsesTheRestAsAPhrase()
    {
        // Der häufigste Zwischenzustand überhaupt: das schließende Anführungszeichen fehlt noch.
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("\"quartal drei");

        Assert.Equal("\"quartal drei\"", plan.MatchExpression, StringComparer.Ordinal);
    }

    [Fact]
    public void Build_WithAnEmptyQuote_ProducesNoTerm()
    {
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("\"\"");

        Assert.Equal(string.Empty, plan.MatchExpression, StringComparer.Ordinal);
    }

    [Fact]
    public void Build_WithAnEscapedQuoteInsideAPhrase_KeepsOneQuote()
    {
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("\"er sagte \"\"halt\"\"\"");

        Assert.Contains("halt", plan.MatchExpression, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithoutInput_ProducesAnEmptyPlan()
    {
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("   ");

        Assert.Equal(string.Empty, plan.MatchExpression, StringComparer.Ordinal);
        Assert.Empty(plan.PathPrefixes);
    }

    [Fact]
    public void Build_WithAMinusInsideAWord_DoesNotNegate()
    {
        // Nur ein Minus am Wortanfang bedeutet Ausschluss — sonst wäre jeder
        // Bindestrich-Begriff wie "e-mail" eine Verneinung.
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("e-mail");

        Assert.DoesNotContain("NOT", plan.MatchExpression, StringComparison.Ordinal);
    }
}
