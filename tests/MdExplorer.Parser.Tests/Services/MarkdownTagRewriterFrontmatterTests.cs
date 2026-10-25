using MdExplorer.Core.Models;
using MdExplorer.Parser.Services;
using MdExplorer.Parser.Tests.Helpers;

namespace MdExplorer.Parser.Tests.Services;

/// <summary>
/// Der Umschreiber ändert im Frontmatter genau das Feld, das der Index als Schlagworte liest:
/// den Schlüssel <c>tags</c> auf oberster Ebene, in jeder Schreibweise und jeder YAML-Form.
/// </summary>
/// <remarks>
/// Der Index liest das Frontmatter mit einem echten YAML-Leser. Arbeitete der Umschreiber nach
/// Zeilenmustern, liefen beide auseinander: Ein <c>Tags:</c> in Großschreibung blieb stehen und
/// kam beim nächsten Lesen als altes Schlagwort zurück, ein eingerücktes <c>tags:</c> in einem
/// anderen Abschnitt oder in einem Textblock wurde dagegen geändert, obwohl es kein Schlagwort
/// ist. Beides ändert Dateien des Nutzers falsch, und zwar projektweit.
/// </remarks>
public sealed class MarkdownTagRewriterFrontmatterTests
{
    private readonly MarkdownTagRewriter _sut = new(new TagNormalizer());

    [Fact]
    public void Apply_WithACapitalisedTagsKey_RewritesTheList()
    {
        const string Markdown = "---\nTags: [alpha, beta]\n---\nText\n";

        string result = _sut.Apply(Markdown, Rename("alpha", "gamma"));

        Assert.Equal("---\nTags: [gamma, beta]\n---\nText\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_WithABlockListWithoutIndentation_RewritesTheItem()
    {
        const string Markdown = "---\ntags:\n- alpha\n- beta\n---\nText\n";

        string result = _sut.Apply(Markdown, Rename("alpha", "gamma"));

        Assert.Equal("---\ntags:\n- gamma\n- beta\n---\nText\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_DeletingFromABlockListWithoutIndentation_RemovesTheLine()
    {
        const string Markdown = "---\ntags:\n- alpha\n- beta\n---\nText\n";

        string result = _sut.Apply(Markdown, Delete("alpha"));

        Assert.Equal("---\ntags:\n- beta\n---\nText\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_WithANestedTagsKey_LeavesItAlone()
    {
        const string Markdown = "---\nmeta:\n  tags: [alpha]\ntags: [beta]\n---\nText\n";

        string result = _sut.Apply(Markdown, Rename("alpha", "gamma"));

        Assert.Equal(Markdown, result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_InsideABlockScalar_LeavesTheUsersTextAlone()
    {
        const string Markdown = "---\ndescription: |\n  tags: alpha\ntags: [beta]\n---\nText\n";

        string result = _sut.Apply(Markdown, Rename("alpha", "gamma"));

        Assert.Equal(Markdown, result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_WithACommentAfterTheFlowList_KeepsBracketAndComment()
    {
        const string Markdown = "---\ntags: [alpha, beta] # Notiz\n---\nText\n";

        string result = _sut.Apply(Markdown, Rename("alpha", "gamma"));

        Assert.Equal("---\ntags: [gamma, beta] # Notiz\n---\nText\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_WithACommentAfterABlockItem_KeepsTheComment()
    {
        const string Markdown = "---\ntags:\n  - alpha # erstes\n  - beta\n---\nText\n";

        string result = _sut.Apply(Markdown, Rename("alpha", "gamma"));

        Assert.Equal("---\ntags:\n  - gamma # erstes\n  - beta\n---\nText\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_DeletingTheFirstFlowItem_KeepsTheOuterSpacing()
    {
        const string Markdown = "---\ntags: [ alpha, beta ]\n---\nText\n";

        string result = _sut.Apply(Markdown, Delete("alpha"));

        Assert.Equal("---\ntags: [ beta ]\n---\nText\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_WithCrlfLineEndings_KeepsThem()
    {
        const string Markdown = "---\r\ntags:\r\n  - alpha\r\n  - beta\r\n---\r\nText\r\n";

        string result = _sut.Apply(Markdown, Delete("alpha"));

        Assert.Equal("---\r\ntags:\r\n  - beta\r\n---\r\nText\r\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_WithAnAlias_LeavesTheFrontmatterAlone()
    {
        // Der Index verwirft ein Frontmatter mit Alias; der Umschreiber liest es dann ebenso nicht.
        const string Markdown = "---\nbase: &b alpha\ntags: [*b, alpha]\n---\nText\n";

        string result = _sut.Apply(Markdown, Rename("alpha", "gamma"));

        Assert.Equal(Markdown, result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_WithBrokenYaml_LeavesTheFrontmatterAlone()
    {
        const string Markdown = "---\ntags: [alpha, beta\n---\nText #alpha\n";

        string result = _sut.Apply(Markdown, Rename("alpha", "gamma"));

        Assert.Equal("---\ntags: [alpha, beta\n---\nText #gamma\n", result, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("neuer name")]
    [InlineData("a, b]")]
    [InlineData("a:b")]
    [InlineData("#")]
    [InlineData("x")]
    public void Apply_WithAReplacementThatIsNoHashtag_Throws(string replacement)
    {
        // Ein solcher Name zerrisse im Text den Hashtag ("#neuer name") oder machte die
        // tags-Zeile ungültig ("[a, b], beta]"), und das gesamte Frontmatter fiele aus dem Index.
        _ = Assert.Throws<ArgumentException>(() => _sut.Apply("Text #alpha\n", Rename("alpha", replacement)));
    }

    [Fact]
    public void Apply_WithAReplacementWithLeadingHash_IsAccepted()
    {
        string result = _sut.Apply("Text #alpha\n", Rename("alpha", "#gamma"));

        Assert.Equal("Text #gamma\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_ToAColourValueInTheBody_LeavesItAlone()
    {
        // #cafe ist für den Index ein Farbwert und kein Schlagwort; umbenannt würde aus der Farbe
        // ein Wort.
        const string Markdown = "---\ntags: [cafe]\n---\nFarbe #cafe\n";

        string result = _sut.Apply(Markdown, Rename("cafe", "kaffee"));

        Assert.Equal("---\ntags: [kaffee]\n---\nFarbe #cafe\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_WhenBodyHashtagsAreNotCounted_LeavesTheBodyAlone()
    {
        // Ist die Auswertung von Hashtags im Text ausgeschaltet, ist eine Raute im Rumpf für den
        // Index kein Schlagwort. Umbenannt wird dann nur das Frontmatter.
        FakeSettingsService settings = new(AppSettings.Default with
        {
            Indexing = AppSettings.Default.Indexing with { AutoExtractHashtags = false },
        });
        MarkdownTagRewriter sut = new(new TagNormalizer(), settings);
        const string Markdown = "---\ntags: [alpha]\n---\nText #alpha\n";

        string result = sut.Apply(Markdown, Rename("alpha", "gamma"));

        Assert.Equal("---\ntags: [gamma]\n---\nText #alpha\n", result, StringComparer.Ordinal);
    }

    private static Dictionary<string, string?> Rename(string from, string to) =>
        new(StringComparer.Ordinal) { [from] = to };

    private static Dictionary<string, string?> Delete(string tag) =>
        new(StringComparer.Ordinal) { [tag] = null };
}
