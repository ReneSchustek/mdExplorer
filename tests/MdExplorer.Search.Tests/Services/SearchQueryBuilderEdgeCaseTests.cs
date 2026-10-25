using MdExplorer.Search.Models;
using MdExplorer.Search.Services;

namespace MdExplorer.Search.Tests.Services;

/// <summary>
/// Randfälle beim Übersetzen der Sucheingabe. Der Übersetzer muss auch halbfertige
/// Eingaben verkraften, denn der Nutzer tippt, und nach jedem Zeichen läuft eine Suche.
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
        // gilt der Rest als gewöhnlicher Suchbegriff. Sonst engte ein versehentliches
        // Leerzeichen die Trefferliste unbemerkt auf ein Schlagwort ein.
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

        // FTS5 schreibt ein Anführungszeichen in einer Phrase verdoppelt; es bleibt genau eines.
        Assert.Equal("\"er sagte \"\"halt\"\"\"", plan.MatchExpression, StringComparer.Ordinal);
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
        // Nur ein Minus am Wortanfang bedeutet Ausschluss, sonst wäre jeder
        // Bindestrich-Begriff wie "e-mail" eine Verneinung.
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("e-mail");

        Assert.Equal("\"e mail\"", plan.MatchExpression, StringComparer.Ordinal);
    }

    /// <remarks>
    /// <para>
    /// Eine Wortgruppe in Anführungszeichen hinter <c>tag:</c> muss als ein Wert beim Schlagwort
    /// ankommen. Käme sie als zwei Dinge an (eine Einschränkung auf <c>zwei</c> und, davon
    /// unabhängig, das zweite Wort irgendwo im Text), enthielte die Trefferliste
    /// Dateien, die mit dem gesuchten Schlagwort nichts zu tun haben.
    /// </para>
    /// <para>
    /// Die Leerstelle bleibt dabei erhalten: Der Index zerlegt den Schlagwort-Namen an
    /// Leerzeichen und Bindestrichen, die Phrase trifft ihn also in beiden Schreibweisen.
    /// </para>
    /// </remarks>
    [Fact]
    public void Build_WithAQuotedTagValue_KeepsThePhraseInOneFilter()
    {
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("tag:\"zwei teile\"");

        Assert.Equal("Tags:\"zwei teile\"", plan.MatchExpression, StringComparer.Ordinal);
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
    /// Die Verneinung allein: FTS5 kennt <c>NOT</c> nur zwischen zwei Ausdrücken, ein
    /// <c>NOT "bericht"</c> wiese es als Syntaxfehler ab. Ohne einen Begriff, von dem
    /// ausgeschlossen wird, entsteht deshalb keine Anfrage.
    /// </remarks>
    [Fact]
    public void Build_WithANegationAsTheOnlyInput_ProducesNoQuery()
    {
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("-bericht");

        Assert.Equal(string.Empty, plan.MatchExpression, StringComparer.Ordinal);
    }

    /// <remarks>
    /// Die Verneinung vor dem Begriff, von dem ausgeschlossen wird: Sie wandert hinter ihn.
    /// </remarks>
    [Fact]
    public void Build_WithANegationBeforeTheTerm_PutsTheExclusionBehindIt()
    {
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("-entwurf bericht");

        Assert.Equal("\"bericht\" NOT \"entwurf\"", plan.MatchExpression, StringComparer.Ordinal);
    }

    /// <remarks>
    /// <c>NOT</c> bindet in FTS5 stärker als <c>OR</c>. Ohne Klammer schlösse der Begriff nur
    /// den letzten Zweig aus.
    /// </remarks>
    [Fact]
    public void Build_WithAnExclusionBehindAnAlternative_ExcludesFromTheWholeAlternative()
    {
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("bericht OR notiz -entwurf");

        Assert.Equal("(\"bericht\" OR \"notiz\") NOT \"entwurf\"", plan.MatchExpression, StringComparer.Ordinal);
    }

    /// <remarks>
    /// Ein Operator am Ende entsteht beim Tippen, bevor der nächste Begriff da ist.
    /// </remarks>
    [Theory]
    [InlineData("bericht OR")]
    [InlineData("bericht AND")]
    [InlineData("bericht NOT")]
    public void Build_WithAnOperatorAtTheEnd_DropsIt(string input)
    {
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build(input);

        Assert.Equal("\"bericht\"", plan.MatchExpression, StringComparer.Ordinal);
    }

    /// <remarks>
    /// Führende Trennzeichen entstehen beim Tippen ständig. Sie dürfen nicht als leeres Wort
    /// in die Anfrage geraten.
    /// </remarks>
    [Theory]
    [InlineData(",,,bericht")]
    [InlineData("...bericht")]
    [InlineData(";;bericht")]
    public void Build_WithLeadingSeparators_FindsTheWordBehindThem(string input)
    {
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build(input);

        Assert.Contains("bericht", plan.MatchExpression, StringComparison.Ordinal);
    }

    /// <remarks>
    /// <c>tag:""</c>, angefangen und wieder gelöscht. Ein leerer Wert darf keine
    /// Schlagwort-Einschränkung erzeugen, sondern gar nichts.
    /// </remarks>
    [Fact]
    public void Build_WithAnEmptyQuotedTagValue_ProducesNoTagFilter()
    {
        SearchQueryBuilder sut = new();

        Fts5QueryPlan plan = sut.Build("tag:\"\"");

        Assert.DoesNotContain("Tags", plan.MatchExpression, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Ein versehentlich eingefügter Text darf keinen MATCH-Ausdruck mit tausenden Begriffen
    /// erzeugen; die Suche läuft nach jedem Tastendruck erneut.
    /// </remarks>
    [Fact]
    public void Build_WithAPastedPage_KeepsAtMostThirtyTwoTerms()
    {
        SearchQueryBuilder sut = new();
        string pasted = string.Join(' ', Enumerable.Range(0, 5_000).Select(index => $"wort{index}"));

        Fts5QueryPlan plan = sut.Build(pasted);

        int terms = plan.MatchExpression.Split(" AND ", StringSplitOptions.None).Length;
        Assert.Equal(32, terms);
    }

    [Fact]
    public void Build_WithAnOverlongPhrase_CutsItTo256Characters()
    {
        SearchQueryBuilder sut = new();
        string phrase = new('a', 900);

        Fts5QueryPlan plan = sut.Build($"\"{phrase}\"");

        // 256 Zeichen plus die beiden Anführungszeichen der FTS5-Phrase.
        Assert.Equal(258, plan.MatchExpression.Length);
    }
}
