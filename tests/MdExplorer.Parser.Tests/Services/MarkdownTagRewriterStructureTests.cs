using MdExplorer.Parser.Services;

namespace MdExplorer.Parser.Tests.Services;

/// <summary>
/// Der Umschreiber ändert genau die Schlagworte, die der Index zählt, und lässt alles
/// Übrige im Dokument des Nutzers Zeichen für Zeichen stehen.
/// </summary>
/// <remarks>
/// Eine Raute im Codeblock, im Inline-Code oder im Ziel eines Verweises ist für den Index kein
/// Schlagwort. Würde sie beim Umbenennen trotzdem ersetzt, änderte die Verwaltung Beispielcode
/// und Sprungmarken in fremden Dateien. In der <c>tags:</c>-Zeile gilt dasselbe für die
/// Einträge, die keine Operation betrifft: Anführungszeichen und Abstände gehören dem Nutzer.
/// </remarks>
public sealed class MarkdownTagRewriterStructureTests
{
    private readonly MarkdownTagRewriter _sut = new(new TagNormalizer());

    [Fact]
    public void Apply_InAFencedCodeBlock_LeavesTheHashtagAlone()
    {
        const string Markdown = "Text mit #alpha.\n\n```\nbeispiel #alpha\n```\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal("Text mit #beta.\n\n```\nbeispiel #alpha\n```\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_InAnIndentedCodeBlock_LeavesTheHashtagAlone()
    {
        const string Markdown = "Text mit #alpha.\n\n    eingerückt #alpha\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal("Text mit #beta.\n\n    eingerückt #alpha\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_InInlineCode_LeavesTheHashtagAlone()
    {
        const string Markdown = "Siehe `#alpha` und #alpha.\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal("Siehe `#alpha` und #beta.\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_InALinkTarget_LeavesTheFragmentAlone()
    {
        const string Markdown = "[Abschnitt](notiz.md#alpha) und #alpha.\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal("[Abschnitt](notiz.md#alpha) und #beta.\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_InAListAndAHeading_RewritesTheHashtag()
    {
        const string Markdown = "## Kopf #alpha\n\n- Punkt #alpha\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal("## Kopf #beta\n\n- Punkt #beta\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_InTheBodyBehindAFrontmatter_RewritesOnlyTheBody()
    {
        const string Markdown = "---\ntitle: Notiz\n---\nText #alpha\n\n---\n\nNoch #alpha\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal("---\ntitle: Notiz\n---\nText #beta\n\n---\n\nNoch #beta\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnAQuotedNeighbourInTheTagLine_KeepsItsQuotes()
    {
        const string Markdown = "---\ntags: [\"projekt\", alpha]\n---\nText.\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal("---\ntags: [\"projekt\", beta]\n---\nText.\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnCompactSpacingInTheTagLine_KeepsTheSpacing()
    {
        const string Markdown = "---\ntags: [projekt,alpha]\n---\nText.\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal("---\ntags: [projekt,beta]\n---\nText.\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnAQuotedRenamedEntry_KeepsTheQuoteStyle()
    {
        const string Markdown = "---\ntags: ['alpha', gamma]\n---\nText.\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal("---\ntags: ['beta', gamma]\n---\nText.\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnATagLineWithoutAffectedEntries_LeavesItByteIdentical()
    {
        const string Markdown = "---\ntags: [gamma,delta]\n---\nText mit #alpha.\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal("---\ntags: [gamma,delta]\n---\nText mit #beta.\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_WhenTheFirstEntryIsDeleted_KeepsTheLineTidy()
    {
        const string Markdown = "---\ntags: [alpha, gamma]\n---\nText.\n";

        string result = _sut.Apply(Markdown, new Dictionary<string, string?>(StringComparer.Ordinal) { ["alpha"] = null });

        Assert.Equal("---\ntags: [gamma]\n---\nText.\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnAQuotedListItem_KeepsTheQuoteStyle()
    {
        const string Markdown = "---\ntags:\n  - \"alpha\"\n  - gamma\n---\nText.\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal("---\ntags:\n  - \"beta\"\n  - gamma\n---\nText.\n", result, StringComparer.Ordinal);
    }

    [Fact]
    public void Apply_OnCrLfLineEndings_RewritesTheBodyInPlace()
    {
        const string Markdown = "Erste #alpha\r\n\r\n```\r\n#alpha\r\n```\r\nLetzte #alpha\r\n";

        string result = _sut.Apply(Markdown, RenameAlphaToBeta());

        Assert.Equal("Erste #beta\r\n\r\n```\r\n#alpha\r\n```\r\nLetzte #beta\r\n", result, StringComparer.Ordinal);
    }

    private static Dictionary<string, string?> RenameAlphaToBeta() =>
        new(StringComparer.Ordinal) { ["alpha"] = "beta" };
}
