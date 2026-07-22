using System.Diagnostics;
using System.Text;
using MdExplorer.Parser.Models;
using MdExplorer.Parser.Services;
using MdExplorer.Parser.Tests.Helpers;

namespace MdExplorer.Parser.Tests.Services;

/// <summary>
/// Edge-Case-Tests fuer den <see cref="MarkdigParser"/>. Decken die
/// pathologischen Eingaben ab, die vom User-Report 2026-06-12 hochgespuelt wurden
/// (Markdig-Renderer-Limit) und sichern die Resilienz gegen ungewoehnliche
/// Eingaben ab — leerer Input, Whitespace-only, BOM, sehr lange Zeilen,
/// Surrogate-Pairs, Null-Byte, kaputtes YAML.
/// </summary>
public sealed class MarkdigParserEdgeCasesTests
{
    private readonly MarkdigParser _sut;

    public MarkdigParserEdgeCasesTests()
    {
        TagNormalizer normalizer = new();
        _sut = new MarkdigParser(
            new FrontmatterExtractor(),
            new TagExtractor(new FakeSettingsService()),
            new WikiLinkExtractor(),
            normalizer);
    }

    [Fact]
    public void Parse_OnWhitespaceOnlyInput_ReturnsEmptyResultWithoutThrow()
    {
        ParseResult result = _sut.Parse("   \n\t  \r\n   ");

        Assert.Empty(result.Frontmatter);
        Assert.Empty(result.Tags);
        Assert.Empty(result.OutlinkSlugs);
    }

    [Fact]
    public void Parse_OnInputWithUtf8Bom_StripsBomAndParsesBody()
    {
        // U+FEFF (BOM) am Anfang — Markdig sollte das tolerieren, der Body-Header bleibt erkennbar.
        string withBom = "﻿# Titel\n\nText mit #tag.";

        ParseResult result = _sut.Parse(withBom);

        string html = GzipHelper.Decompress(result.RenderedHtmlGz);
        Assert.Contains("Titel", html, StringComparison.Ordinal);
        Assert.Contains("tag", result.Tags);
    }

    [Fact]
    public void Parse_OnVeryLongSingleLine_DoesNotCrash()
    {
        // 100k Zeichen ohne Newline.
        string longLine = new('a', 100_000);

        ParseResult result = _sut.Parse(longLine);

        // Wichtig: kein Throw. Inhalt landet im gerenderten HTML.
        string html = GzipHelper.Decompress(result.RenderedHtmlGz);
        Assert.Contains("aaaaa", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_OnSurrogatePairs_PreservesUnicodeContent()
    {
        // Emoji + CJK + Mathematischer Operator (alle als Surrogate-Pair encoded in UTF-16).
        const string Markdown = "# Test 🚀 用户 ∀x∃y\n\nText mit #emoji-🎉 Tag.";

        ParseResult result = _sut.Parse(Markdown);

        string html = GzipHelper.Decompress(result.RenderedHtmlGz);
        Assert.Contains("🚀", html, StringComparison.Ordinal);
        Assert.Contains("用户", html, StringComparison.Ordinal);
        Assert.Contains("∀x∃y", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_OnNullByteInBody_DoesNotCrash()
    {
        // C0-Control NUL — pathologisch, sollte aber den Parser nicht killen.
        string withNul = "# Titel\n\nText mit \0 in der Mitte.";

        ParseResult result = _sut.Parse(withNul);

        // Inhalt landet entweder mit oder ohne NUL im HTML — wichtig ist nur, dass kein Throw kommt.
        Assert.NotNull(result.RenderedHtmlGz.ToArray());
    }

    [Fact]
    public void Parse_OnInvalidYamlFrontmatter_DoesNotCrashAndDelegatesGracefully()
    {
        // Kaputtes YAML ('key: : :').
        const string Markdown = """
            ---
            key: : :
            tags: ohne_klammern
            ---
            # Body bleibt parsebar.
            """;

        // Erwartung: KEIN Throw aus der Parse-Methode selbst. Wenn der
        // FrontmatterExtractor das YAML schluckt, bleibt Frontmatter leer; rendert er einen
        // Teil, ist das Verhalten dokumentiert. Wichtig: Parser darf nicht sterben.
        ParseResult result = _sut.Parse(Markdown);

        Assert.NotNull(result);
        string html = GzipHelper.Decompress(result.RenderedHtmlGz);
        Assert.Contains("Body bleibt parsebar", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void Parse_OnHugeInput_StaysUnderFiveSeconds()
    {
        // 5 MB Markdown via wiederholten Header.
        StringBuilder builder = new(5 * 1024 * 1024);
        for (int i = 0; i < 100_000; i++)
        {
            _ = builder.Append("# Header ").Append(i).Append('\n');
        }
        string huge = builder.ToString();

        Stopwatch stopwatch = Stopwatch.StartNew();
        ParseResult result = _sut.Parse(huge);
        stopwatch.Stop();

        Assert.NotNull(result);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Huge-Input-Parse benoetigte {stopwatch.Elapsed.TotalSeconds:F2}s, Budget 5s.");
    }

    [Fact]
    public void Parse_OnRepeatedEmphasisMarkers_DoesNotCrash()
    {
        // 200 abwechselnde Emphasis-Marker — Markdig waehlt seine eigenen
        // Pairing-Heuristiken, soll aber nicht crashen.
        StringBuilder builder = new(800);
        for (int i = 0; i < 200; i++)
        {
            _ = builder.Append("*a_b ");
        }

        ParseResult result = _sut.Parse(builder.ToString());

        Assert.NotNull(result);
    }

    [Fact]
    public void Parse_OnDeeplyNestedDelimiters_EitherThrowsArgumentExceptionOrSucceeds()
    {
        // 1500 verschachtelte `[`-`]`-Paare. Markdigs Renderer hat ein Depth-Limit
        // — dieser Test dokumentiert das Verhalten:
        //   - Entweder rendert Markdig sauber durch (kleinere Tiefe).
        //   - Oder es wirft ArgumentException("depth limit exceeded") — dann fangen
        //     wir das in ParseOrchestrator.
        // Beide Pfade sind akzeptabel; wichtig ist, dass keine andere Exception kommt.
        StringBuilder builder = new(3000);
        for (int i = 0; i < 1500; i++)
        {
            _ = builder.Append('[');
        }
        _ = builder.Append("text");
        for (int i = 0; i < 1500; i++)
        {
            _ = builder.Append(']');
        }
        string deep = builder.ToString();

        try
        {
            _ = _sut.Parse(deep);
        }
        catch (ArgumentException ex)
        {
            Assert.Contains("depth", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Parse_OnInputWithMixedLineEndings_NormalizesAndParses()
    {
        // \r, \r\n, \n in einem Dokument — Markdig akzeptiert das, wir wollen sicherstellen,
        // dass die Tag-Extraktion alle Tags findet.
        const string Markdown = "Header\r\n\r\n#tag1 erste Zeile.\rText\n#tag2 dritte Zeile.";

        ParseResult result = _sut.Parse(Markdown);

        Assert.Contains("tag1", result.Tags);
        Assert.Contains("tag2", result.Tags);
    }

    [Fact]
    public void Parse_OnEmptyFrontmatterMarker_ReturnsEmptyFrontmatterAndBody()
    {
        // Frontmatter-Marker vorhanden, aber leer.
        const string Markdown = """
            ---
            ---
            Body folgt.
            """;

        ParseResult result = _sut.Parse(Markdown);

        Assert.Empty(result.Frontmatter);
        string html = GzipHelper.Decompress(result.RenderedHtmlGz);
        Assert.Contains("Body folgt", html, StringComparison.Ordinal);
    }
}
