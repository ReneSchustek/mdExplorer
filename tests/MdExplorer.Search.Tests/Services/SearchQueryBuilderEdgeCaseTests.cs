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
    /// <remarks>
    /// <para>
    /// <c>tag:"zwei woerter"</c> — die Wortgruppe muss als **ein** Wert beim Schlagwort
    /// ankommen. Bis zum 16.08.2026 kam sie als zwei Dinge an: eine Einschränkung auf
    /// <c>zwei</c> und, davon unabhängig, das Wort <c>woerter</c> irgendwo im Text. Die
    /// Trefferliste enthielt damit Dateien, die mit dem gesuchten Schlagwort nichts zu tun
    /// hatten.
    /// </para>
    /// <para>
    /// Dass die Leerstelle dabei wegfällt, ist gewollt und kein zweiter Fehler: Ein Schlagwort
    /// ist ein Slug und enthält keine. Der Wert ist danach ohne Treffer — aber die Anfrage
    /// fragt das, was dasteht, und nicht zwei Dinge auf einmal.
    /// </para>
    /// </remarks>
    [Fact]
    public void Build_WithAQuotedTagValue_KeepsThePhraseInOneFilter()
    {
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("tag:\"zwei woerter\"");

        Assert.Equal("Tags:\"zweiwoerter\"", plan.MatchExpression, StringComparer.Ordinal);
    }

    /// <remarks>
    /// Dasselbe für die Pfad-Einschränkung: Ordnernamen enthalten Leerzeichen.
    /// </remarks>
    [Fact]
    public void Build_WithAQuotedPathValue_KeepsThePathTogether()
    {
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("path:\"mein ordner\"");

        Assert.Equal("mein ordner", Assert.Single(plan.PathPrefixes), StringComparer.Ordinal);
    }

    /// <remarks>
    /// Die Verneinung am Anfang: Ohne vorangehenden Begriff darf kein führendes Leerzeichen
    /// entstehen, und die Anfrage muss trotzdem gültig sein.
    /// </remarks>
    [Fact]
    public void Build_WithANegationAsTheOnlyInput_StillProducesAValidQuery()
    {
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("-bericht");

        Assert.StartsWith("NOT ", plan.MatchExpression, StringComparison.Ordinal);
        Assert.DoesNotContain("  ", plan.MatchExpression, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Führende Trennzeichen entstehen beim Tippen ständig. Sie dürfen nicht als leeres Wort
    /// in die Anfrage geraten.
    /// </remarks>
    [Theory]
    [InlineData(",,,bericht")]
    [InlineData("...bericht")]
    [InlineData(";;bericht")]
    public void Build_WithLeadingSeparators_FindsTheWordBehindThem(string eingabe)
    {
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build(eingabe);

        Assert.Contains("bericht", plan.MatchExpression, StringComparison.Ordinal);
    }

    /// <remarks>
    /// <c>tag:""</c> — angefangen und wieder gelöscht. Ein leerer Wert darf keine
    /// Schlagwort-Einschränkung erzeugen, sondern gar nichts.
    /// </remarks>
    [Fact]
    public void Build_WithAnEmptyQuotedTagValue_ProducesNoTagFilter()
    {
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("tag:\"\"");

        Assert.DoesNotContain("Tags", plan.MatchExpression, StringComparison.Ordinal);
    }
}
