using MdExplorer.Parser.Services;

namespace MdExplorer.Parser.Tests.Services;

/// <summary>
/// Tests fuer den Tag-Rewriter: pruefen Body-Regex, YAML-Frontmatter (inline + block),
/// Rename / Merge / Delete und Edge-Cases (Code-Bloecke werden NICHT geschont — Indexer-
/// Re-Sync triggert ohnehin neuen Parse).
/// </summary>
public sealed class MarkdownTagRewriterTests
{
    private readonly MarkdownTagRewriter _sut;

    public MarkdownTagRewriterTests()
    {
        _sut = new MarkdownTagRewriter(new TagNormalizer());
    }

    [Fact]
    public void Apply_OnEmptyOperations_ReturnsOriginal()
    {
        const string Markdown = "Body mit #foo Tag.";
        string result = _sut.Apply(Markdown, new Dictionary<string, string?>(StringComparer.Ordinal));
        Assert.Equal(Markdown, result);
    }

    [Fact]
    public void Apply_RenamesBodyTag()
    {
        const string Markdown = "Body mit #projekt-a und ein #other.";
        string result = _sut.Apply(Markdown, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["projekt-a"] = "projektA",
        });
        Assert.Equal("Body mit #projektA und ein #other.", result);
    }

    [Fact]
    public void Apply_DeletesBodyTag()
    {
        const string Markdown = "Vor #foo nach.";
        string result = _sut.Apply(Markdown, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["foo"] = null,
        });
        Assert.Equal("Vor  nach.", result);
    }

    [Fact]
    public void Apply_PreservesAdjacentText()
    {
        const string Markdown = "Praefix#nottag ist nicht #echt-tag.";
        string result = _sut.Apply(Markdown, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["echt-tag"] = "neutag",
        });
        Assert.Equal("Praefix#nottag ist nicht #neutag.", result);
    }

    [Fact]
    public void Apply_MatchesCaseInsensitivelyBySlug()
    {
        const string Markdown = "Tags: #Projekt-A und #PROJEKT-A.";
        string result = _sut.Apply(Markdown, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["projekt-a"] = "projektA",
        });
        Assert.Equal("Tags: #projektA und #projektA.", result);
    }

    [Fact]
    public void Apply_RewritesFrontmatterBlockSequence()
    {
        const string Markdown = "---\ntitle: Beispiel\ntags:\n  - foo\n  - bar\n  - baz\n---\nBody.\n";
        string result = _sut.Apply(Markdown, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["foo"] = "foo-neu",
            ["bar"] = null,
        });
        const string Expected = "---\ntitle: Beispiel\ntags:\n  - foo-neu\n  - baz\n---\nBody.\n";
        Assert.Equal(Expected, result);
    }

    [Fact]
    public void Apply_RewritesFrontmatterInlineList()
    {
        const string Markdown = "---\ntags: [foo, bar, baz]\n---\nBody.\n";
        string result = _sut.Apply(Markdown, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["bar"] = null,
            ["foo"] = "foo-neu",
        });
        const string Expected = "---\ntags: [foo-neu, baz]\n---\nBody.\n";
        Assert.Equal(Expected, result);
    }

    [Fact]
    public void Apply_RewritesFrontmatterCommaSeparated()
    {
        const string Markdown = "---\ntags: foo, bar\n---\nBody.\n";
        string result = _sut.Apply(Markdown, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["foo"] = "neu",
        });
        const string Expected = "---\ntags: neu, bar\n---\nBody.\n";
        Assert.Equal(Expected, result);
    }

    [Fact]
    public void Apply_MergeDedupesFrontmatterInline()
    {
        const string Markdown = "---\ntags: [source, target]\n---\nBody.\n";
        string result = _sut.Apply(Markdown, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["source"] = "target",
        });
        const string Expected = "---\ntags: [target]\n---\nBody.\n";
        Assert.Equal(Expected, result);
    }

    [Fact]
    public void Apply_MergeDedupesFrontmatterBlock()
    {
        const string Markdown = "---\ntags:\n  - source\n  - target\n---\nBody.\n";
        string result = _sut.Apply(Markdown, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["source"] = "target",
        });
        const string Expected = "---\ntags:\n  - target\n---\nBody.\n";
        Assert.Equal(Expected, result);
    }

    [Fact]
    public void Apply_PreservesUnrelatedFrontmatterKeys()
    {
        const string Markdown = "---\ntitle: Beispiel\nauthor: Anna\ntags: [foo]\ndate: 2026-06-10\n---\nBody.\n";
        string result = _sut.Apply(Markdown, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["foo"] = "neu",
        });
        const string Expected = "---\ntitle: Beispiel\nauthor: Anna\ntags: [neu]\ndate: 2026-06-10\n---\nBody.\n";
        Assert.Equal(Expected, result);
    }

    [Fact]
    public void Apply_RewritesBodyAndFrontmatterInOnePass()
    {
        const string Markdown = "---\ntags: [foo]\n---\nBody mit #foo und #foo.\n";
        string result = _sut.Apply(Markdown, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["foo"] = "neu",
        });
        const string Expected = "---\ntags: [neu]\n---\nBody mit #neu und #neu.\n";
        Assert.Equal(Expected, result);
    }

    [Fact]
    public void Apply_ReturnsOriginalWhenNothingMatches()
    {
        const string Markdown = "---\ntitle: x\n---\nBody ohne Tags.\n";
        string result = _sut.Apply(Markdown, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["foo"] = "bar",
        });
        Assert.Same(Markdown, result);
    }

    [Fact]
    public void Apply_HandlesCrLfLineEndings()
    {
        const string Markdown = "---\r\ntags:\r\n  - foo\r\n---\r\nBody mit #foo.\r\n";
        string result = _sut.Apply(Markdown, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["foo"] = "neu",
        });
        const string Expected = "---\r\ntags:\r\n  - neu\r\n---\r\nBody mit #neu.\r\n";
        Assert.Equal(Expected, result);
    }
}
