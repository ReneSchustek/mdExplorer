using MdExplorer.Parser.Services;

namespace MdExplorer.Parser.Tests.Services;

/// <summary>
/// Randfälle beim Umschreiben von Schlagworten. Der Umschreiber greift direkt in Dateien des
/// Nutzers ein; jeder Fall, in dem er einen Block falsch erkennt, verändert oder zerstört
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

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal(Markdown, result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnAHorizontalRuleFirstLine_TreatsItAsBodyText()
    {
        // "----" ist eine Trennlinie, kein Frontmatter-Anfang.
        const string Markdown = "----\nText mit #alpha.\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal("----\nText mit #beta.\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnAnUnclosedFrontmatter_TreatsEverythingAsBody()
    {
        // Kommt bei halb getippten Dateien vor. Der Umschreiber darf den Block dann nicht
        // erraten, sondern muss den Text als gewöhnlichen Inhalt behandeln.
        const string Markdown = "---\ntags: [alpha]\nText mit #alpha.\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal("---\ntags: [alpha]\nText mit #beta.\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnAFrontmatterWithoutATrailingNewline_TreatsEverythingAsBody()
    {
        const string Markdown = "---\ntags: [alpha]";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal(Markdown, result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnFrontmatterWithoutABody_KeepsTheFileValid()
    {
        const string Markdown = "---\ntags: [alpha]\n---\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal("---\ntags: [beta]\n---\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_WithAClosingDotsMarker_RecognizesTheFrontmatter()
    {
        // YAML erlaubt "..." als Abschluss. Wird der nicht erkannt, bliebe der Block ungeändert.
        const string Markdown = "---\ntags: [alpha]\n...\nText.\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal("---\ntags: [beta]\n...\nText.\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnQuotedFrontmatterTags_StripsTheQuotesBeforeMatching()
    {
        const string Markdown = "---\ntags: [\"alpha\", 'gamma']\n---\nText.\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Contains("beta", result, StringComparison.Ordinal);
        Assert.DoesNotContain("alpha", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_OnAnEmptyEntryInABracketList_LeavesTheListAlone()
    {
        // Ein doppeltes Komma in eckigen Klammern ist kaputtes YAML; der Index liest dieses
        // Frontmatter nicht, also ist dort auch kein Schlagwort umzubenennen.
        const string Markdown = "---\ntags: [alpha, , gamma]\n---\nText.\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal(Markdown, result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnAnEmptyEntryInACommaList_DropsIt()
    {
        // Ohne Klammern ist dieselbe Zeile ein Text, den der Index an den Kommas teilt.
        const string Markdown = "---\ntags: alpha, , gamma\n---\nText.\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal("---\ntags: beta, gamma\n---\nText.\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnATagListWithoutAMatch_LeavesTheLineByteIdentical()
    {
        const string Markdown = "---\ntags: [gamma, delta]\n---\nText.\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal(Markdown, result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnAHashWithoutAName_LeavesItAlone()
    {
        // "#" allein ist eine Überschrift, kein Tag.
        const string Markdown = "# Überschrift\n\nText mit #alpha.\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal("# Überschrift\n\nText mit #beta.\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnAFrontmatterEntryWithoutAUsableName_KeepsIt()
    {
        // "..." als Tag-Wert ergibt keinen Bezeichner. Der Eintrag muss stehen bleiben,
        // statt beim Umbenennen still zu verschwinden.
        const string Markdown = "---\ntags:\n  - \"...\"\n  - alpha\n---\nText.\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Contains("...", result, StringComparison.Ordinal);
        Assert.Contains("beta", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_OnAnEmptyDocument_ReturnsItUnchanged()
    {
        string result = _sut.Apply(string.Empty, RenameAlphaToBeta());

        Assert.Equal(string.Empty, result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_WithABlankOperationKey_Throws()
    {
        Dictionary<string, string?> operations = new(StringComparer.Ordinal) { ["   "] = "beta" };

        _ = Assert.Throws<ArgumentException>(() => _sut.Apply("Text", operations));
    }

    private static Dictionary<string, string?> RenameAlphaToBeta() =>
        new(StringComparer.Ordinal) { ["alpha"] = "beta" };
}
