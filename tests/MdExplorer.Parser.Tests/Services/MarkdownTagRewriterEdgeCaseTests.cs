using MdExplorer.Parser.Services;

namespace MdExplorer.Parser.Tests.Services;

/// <summary>
/// Prüft die Randfälle des Tag-Umschreibens. Der Umschreiber greift direkt in Dateien des
/// Nutzers ein — jeder Fall, in dem er einen Block falsch erkennt, verändert oder zerstört
/// fremden Inhalt. Deshalb sind hier vor allem die Formen abgedeckt, die <em>keine</em>
/// Änderung auslösen dürfen: halbe Frontmatter-Blöcke, leere Einträge und Zeichenfolgen,
/// die zwar wie ein Tag aussehen, aber keinen gültigen Bezeichner ergeben.
/// </summary>
public sealed class MarkdownTagRewriterEdgeCaseTests
{
    private readonly MarkdownTagRewriter _sut = new(new TagNormalizer());

    [Fact]
    public void Apply_OnFrontmatterWithoutALineBreak_LeavesTheTextUntouched()
    {
        const string Markdown = "---";

        string ergebnis = _sut.Apply(Markdown, Umbenennen());

        Assert.Equal(Markdown, ergebnis, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnAHorizontalRuleFirstLine_TreatsItAsBodyText()
    {
        // "----" ist eine Trennlinie, kein Frontmatter-Anfang.
        const string Markdown = "----\nText mit #alpha.\n";

        string ergebnis = _sut.Apply(Markdown, Umbenennen());

        Assert.Equal("----\nText mit #beta.\n", ergebnis, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnAnUnclosedFrontmatter_TreatsEverythingAsBody()
    {
        // Kommt bei halb getippten Dateien vor. Der Umschreiber darf den Block dann nicht
        // erraten, sondern muss den Text als gewöhnlichen Inhalt behandeln.
        const string Markdown = "---\ntags: [alpha]\nText mit #alpha.\n";

        string ergebnis = _sut.Apply(Markdown, Umbenennen());

        Assert.Equal("---\ntags: [alpha]\nText mit #beta.\n", ergebnis, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnAFrontmatterWithoutATrailingNewline_TreatsEverythingAsBody()
    {
        const string Markdown = "---\ntags: [alpha]";

        string ergebnis = _sut.Apply(Markdown, Umbenennen());

        Assert.Equal(Markdown, ergebnis, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnFrontmatterWithoutABody_KeepsTheFileValid()
    {
        const string Markdown = "---\ntags: [alpha]\n---\n";

        string ergebnis = _sut.Apply(Markdown, Umbenennen());

        Assert.Equal("---\ntags: [beta]\n---\n", ergebnis, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_WithAClosingDotsMarker_RecognizesTheFrontmatter()
    {
        // YAML erlaubt "..." als Abschluss — wird der nicht erkannt, bliebe der Block ungeändert.
        const string Markdown = "---\ntags: [alpha]\n...\nText.\n";

        string ergebnis = _sut.Apply(Markdown, Umbenennen());

        Assert.Equal("---\ntags: [beta]\n...\nText.\n", ergebnis, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnQuotedFrontmatterTags_StripsTheQuotesBeforeMatching()
    {
        const string Markdown = "---\ntags: [\"alpha\", 'gamma']\n---\nText.\n";

        string ergebnis = _sut.Apply(Markdown, Umbenennen());

        Assert.Contains("beta", ergebnis, StringComparison.Ordinal);
        Assert.DoesNotContain("alpha", ergebnis, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_OnAnEmptyEntryInTheTagList_DropsIt()
    {
        // Ein doppeltes Komma entsteht leicht beim Bearbeiten von Hand.
        const string Markdown = "---\ntags: [alpha, , gamma]\n---\nText.\n";

        string ergebnis = _sut.Apply(Markdown, Umbenennen());

        Assert.Equal("---\ntags: [beta, gamma]\n---\nText.\n", ergebnis, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnATagListWithoutAMatch_LeavesTheLineByteIdentical()
    {
        const string Markdown = "---\ntags: [gamma, delta]\n---\nText.\n";

        string ergebnis = _sut.Apply(Markdown, Umbenennen());

        Assert.Equal(Markdown, ergebnis, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnAHashWithoutAName_LeavesItAlone()
    {
        // "#" allein ist eine Überschrift, kein Tag.
        const string Markdown = "# Überschrift\n\nText mit #alpha.\n";

        string ergebnis = _sut.Apply(Markdown, Umbenennen());

        Assert.Equal("# Überschrift\n\nText mit #beta.\n", ergebnis, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnAFrontmatterEntryWithoutAUsableName_KeepsIt()
    {
        // "---" als Tag-Wert ergibt keinen Bezeichner. Der Eintrag muss stehen bleiben,
        // statt beim Umbenennen still zu verschwinden.
        const string Markdown = "---\ntags:\n  - \"...\"\n  - alpha\n---\nText.\n";

        string ergebnis = _sut.Apply(Markdown, Umbenennen());

        Assert.Contains("...", ergebnis, StringComparison.Ordinal);
        Assert.Contains("beta", ergebnis, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_OnAnEmptyDocument_ReturnsItUnchanged()
    {
        string ergebnis = _sut.Apply(string.Empty, Umbenennen());

        Assert.Equal(string.Empty, ergebnis, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_WithABlankOperationKey_Throws()
    {
        Dictionary<string, string?> vorgang = new(StringComparer.Ordinal) { ["   "] = "beta" };

        _ = Assert.Throws<ArgumentException>(() => _sut.Apply("Text", vorgang));
    }

    private static Dictionary<string, string?> Umbenennen() =>
        new(StringComparer.Ordinal) { ["alpha"] = "beta" };
}
