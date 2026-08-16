using Markdig.Syntax;
using MdExplorer.Parser.Services;
using MdExplorer.Parser.Tests.Helpers;

namespace MdExplorer.Parser.Tests.Services;

/// <summary>
/// Hält fest, dass Indizierung und Umbenennung dieselbe Regel benutzen.
/// </summary>
/// <remarks>
/// Bis zum 16.08.2026 taten sie es nicht: Der Extractor kannte keine abschließende Bedingung,
/// der Rewriter schon. Bei <c>#notizé</c> hieß das — indiziert wurde <c>notiz</c>, umbenannt
/// wurde nichts. Die Datei behielt den alten Namen, der Index bekam ihn beim nächsten Lauf
/// erneut, und niemand erfuhr davon.
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
    /// Der eigentliche Befund. Was der eine nicht ersetzen kann, darf der andere nicht
    /// aufnehmen — sonst laufen Datei und Index auseinander.
    /// </remarks>
    [Theory]
    [InlineData("Ein #notizé mitten im Wort.")]
    [InlineData("Ein #tagé und noch etwas.")]
    [InlineData("Ein #wortß́ mit fremdem Zeichen.")]
    public void WhatTheRewriterCannotReachIsNotIndexed(string source)
    {
        MarkdownDocument ast = TestPipelineFactory.Parse(source);

        Assert.Empty(_extractor.ExtractFromAst(ast));
    }

    /// <remarks>
    /// Der Bindestrich am Ende gehört noch zum Treffer — der Slug-Erzeuger schneidet ihn ab,
    /// und beide Seiten kommen damit auf dieselbe Kennung. Kein Auseinanderlaufen, deshalb
    /// hier ausdrücklich als erlaubter Fall festgehalten.
    /// </remarks>
    [Fact]
    public void ATrailingHyphenStaysPartOfTheMatch()
    {
        MarkdownDocument ast = TestPipelineFactory.Parse("Ein #tag- mit Bindestrich am Ende.");

        Assert.Equal("tag-", Assert.Single(_extractor.ExtractFromAst(ast)));
        Assert.Equal("tag", new TagNormalizer().ToSlug("tag-"));
    }

    [Fact]
    public void ExtractorAndRewriterAgreeOnAPlainTag()
    {
        MarkdownDocument ast = TestPipelineFactory.Parse("Text mit #notiz darin.");

        Assert.Equal("notiz", Assert.Single(_extractor.ExtractFromAst(ast)));
        Assert.Equal(
            "Text mit #merkzettel darin.",
            _rewriter.Apply("Text mit #notiz darin.", new Dictionary<string, string?> { ["notiz"] = "merkzettel" }));
    }

    /// <remarks>
    /// Farbwerte erfüllen jede Bedingung eines Schlagworts: erstes Zeichen ein Buchstabe, der
    /// Rest erlaubt. Der Index trug 42 davon, <c>#F59E0B</c> allein an 21 Dateien — und
    /// getroffen hat es ausgerechnet die Dokumentation der Gestaltungslinie.
    /// </remarks>
    [Theory]
    [InlineData("Warning-600 #F59E0B in der Palette.")]
    [InlineData("Rot ist #FF0000 und sonst nichts.")]
    [InlineData("Mit Deckkraft: #FFAA33CC hier.")]
    [InlineData("Kurzform #ABC steht auch dort.")]
    public void ColorLiteralsAreNotTags(string source)
    {
        MarkdownDocument ast = TestPipelineFactory.Parse(source);

        Assert.Empty(_extractor.ExtractFromAst(ast));
    }

    /// <remarks>
    /// Der bewusst in Kauf genommene Verlust, damit ihn niemand später für einen Fehler hält:
    /// Wörter aus lauter Hexziffern fallen mit weg. In deutschsprachiger Dokumentation ist
    /// das die seltenere Sorte Schlagwort; wer sie braucht, nimmt die Länge aus der Liste.
    /// </remarks>
    [Theory]
    [InlineData("facade")]
    [InlineData("decade")]
    [InlineData("b2b")]
    public void HexWordsAreLostOnPurpose(string word)
    {
        Assert.True(HashtagPattern.IsColorLiteral(word));
    }

    [Theory]
    [InlineData("notiz")]
    [InlineData("projekt")]
    [InlineData("f59e0")]
    [InlineData("F59E0B7")]
    public void EverythingElseStaysATag(string word)
    {
        Assert.False(HashtagPattern.IsColorLiteral(word));
    }

    [Fact]
    public void ForTagRejectsAnEmptyName()
    {
        _ = Assert.Throws<ArgumentException>(() => HashtagPattern.ForTag("  "));
    }

    [Fact]
    public void ForTagMatchesTheSameBoundariesAsTheSharedExpression()
    {
        Assert.Equal(1, HashtagPattern.ForTag("notiz").Count("Text mit #notiz darin."));
        Assert.Equal(0, HashtagPattern.ForTag("notiz").Count("Text mit #notizé darin."));
        Assert.Equal(0, HashtagPattern.ForTag("notiz").Count("Kein Treffer in ##notiz."));
    }
}
