using Markdig.Syntax;
using MdExplorer.Parser.Services;
using MdExplorer.Parser.Tests.Helpers;

namespace MdExplorer.Parser.Tests.Services;

/// <summary>
/// Hält fest, dass Indizierung und Umbenennung dieselbe Regel benutzen.
/// </summary>
/// <remarks>
/// Kennt der Extractor keine abschließende Bedingung, der Rewriter aber schon, wird bei
/// <c>#notizé</c> <c>notiz</c> indiziert und nichts umbenannt. Die Datei behielte den alten
/// Namen, der Index bekäme ihn beim nächsten Lauf erneut, und niemand erführe davon.
/// </remarks>
public sealed class HashtagPatternTests
{
    private readonly FakeSettingsService _settings = new();
    private readonly TagExtractor _extractor;
    private readonly MarkdownTagRewriter _rewriter = new(new TagNormalizer());

    public HashtagPatternTests()
    {
        _extractor = new TagExtractor(_settings);
    }

    /// <remarks>
    /// Was der eine nicht ersetzen kann, darf der andere nicht
    /// aufnehmen, sonst laufen Datei und Index auseinander.
    /// </remarks>
    [Theory]
    [InlineData("Ein #notizé mitten im Wort.")]
    [InlineData("Ein #tagé und noch etwas.")]
    public void ExtractFromAst_WithALetterOutsideTheRule_IndexesNothing(string source)
    {
        MarkdownDocument ast = TestPipelineFactory.Parse(source);

        Assert.Empty(_extractor.ExtractFromAst(ast));
    }

    /// <remarks>
    /// macOS speichert „ä“ oft als „a“ mit nachgestellten Punkten (NFD). Der Index liest den Namen
    /// in der zusammengesetzten Form, und der Umschreiber findet die Raute in der Datei trotzdem:
    /// Beide vergleichen über denselben Slug.
    /// </remarks>
    [Fact]
    public void ExtractAndApply_OnADecomposedUmlaut_Agree()
    {
        const string Source = "Ein #Bär hier.";

        Assert.Equal("Bär", Assert.Single(_extractor.ExtractFromAst(TestPipelineFactory.Parse(Source))));
        Assert.Equal(
            "Ein #honig hier.",
            _rewriter.Apply(Source, new Dictionary<string, string?>(StringComparer.Ordinal) { ["bär"] = "honig" }));
    }

    /// <remarks>
    /// Der Bindestrich am Ende gehört noch zum Treffer; der Slug-Erzeuger schneidet ihn ab,
    /// und beide Seiten kommen damit auf dieselbe Kennung. Kein Auseinanderlaufen, deshalb
    /// hier ausdrücklich als erlaubter Fall festgehalten.
    /// </remarks>
    [Fact]
    public void ExtractFromAst_WithATrailingHyphen_KeepsItInTheMatch()
    {
        MarkdownDocument ast = TestPipelineFactory.Parse("Ein #tag- mit Bindestrich am Ende.");

        Assert.Equal("tag-", Assert.Single(_extractor.ExtractFromAst(ast)));
        Assert.Equal("tag", new TagNormalizer().ToSlug("tag-"));
    }

    [Fact]
    public void ExtractAndApply_OnAPlainTag_Agree()
    {
        MarkdownDocument ast = TestPipelineFactory.Parse("Text mit #notiz darin.");

        Assert.Equal("notiz", Assert.Single(_extractor.ExtractFromAst(ast)));
        Assert.Equal(
            "Text mit #merkzettel darin.",
            _rewriter.Apply("Text mit #notiz darin.", new Dictionary<string, string?>(StringComparer.Ordinal) { ["notiz"] = "merkzettel" }));
    }

    /// <remarks>
    /// Farbwerte erfüllen jede Bedingung eines Schlagworts: erstes Zeichen ein Buchstabe, der
    /// Rest erlaubt. In einem Bestand mit Gestaltungsdokumentation ergab das 42 Schlagworte aus
    /// Farbcodes, <c>#F59E0B</c> allein an 21 Dateien.
    /// </remarks>
    [Theory]
    [InlineData("Warning-600 #F59E0B in der Palette.")]
    [InlineData("Rot ist #FF0000 und sonst nichts.")]
    [InlineData("Mit Deckkraft: #FFAA33CC hier.")]
    [InlineData("Kurzform #ABC steht auch dort.")]
    public void ExtractFromAst_OnAColourValue_IndexesNothing(string source)
    {
        MarkdownDocument ast = TestPipelineFactory.Parse(source);

        Assert.Empty(_extractor.ExtractFromAst(ast));
    }

    /// <remarks>
    /// Der in Kauf genommene Verlust, damit ihn niemand später für einen Fehler hält:
    /// Wörter aus lauter Hexziffern fallen mit weg. In deutschsprachiger Dokumentation ist
    /// das die seltenere Sorte Schlagwort; wer sie braucht, nimmt die Länge aus der Liste.
    /// </remarks>
    [Theory]
    [InlineData("facade")]
    [InlineData("decade")]
    [InlineData("b2b")]
    public void IsColorLiteral_OnAWordOfHexDigits_ReturnsTrue(string word)
    {
        Assert.True(HashtagPattern.IsColorLiteral(word));
    }

    [Theory]
    [InlineData("notiz")]
    [InlineData("projekt")]
    [InlineData("f59e0")]
    [InlineData("F59E0B7")]
    public void IsColorLiteral_OnOtherWords_ReturnsFalse(string word)
    {
        Assert.False(HashtagPattern.IsColorLiteral(word));
    }

    [Theory]
    [InlineData("notiz", true)]
    [InlineData("#notiz", true)]
    [InlineData("Projekt-Alpha_2", true)]
    [InlineData("übersicht", true)]
    [InlineData("neuer name", false)]
    [InlineData("a, b]", false)]
    [InlineData("2026", false)]
    [InlineData("x", false)]
    [InlineData("", false)]
    public void IsValidName_OnSampleNames_FollowsTheSameRuleAsTheText(string name, bool expected)
    {
        Assert.Equal(expected, HashtagPattern.IsValidName(name));
    }

    [Fact]
    public void ForTag_WithAnEmptyName_Throws()
    {
        _ = Assert.Throws<ArgumentException>(() => HashtagPattern.ForTag("  "));
    }

    [Fact]
    public void ForTag_OnSampleTexts_UsesTheSameBoundariesAsTheSharedExpression()
    {
        Assert.Equal(1, HashtagPattern.ForTag("notiz").Count("Text mit #notiz darin."));
        Assert.Equal(0, HashtagPattern.ForTag("notiz").Count("Text mit #notizé darin."));
        Assert.Equal(0, HashtagPattern.ForTag("notiz").Count("Kein Treffer in ##notiz."));
    }
}
