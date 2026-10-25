using System.Diagnostics;
using System.Text;
using MdExplorer.Parser.Models;
using MdExplorer.Parser.Services;
using MdExplorer.Parser.Tests.Helpers;

namespace MdExplorer.Parser.Tests.Services;

/// <summary>
/// Der <see cref="MarkdigParser"/> übersteht ungewöhnliche Eingaben: nur Leerraum, BOM, sehr lange
/// Zeilen, Zeichen außerhalb der Grundebene, ein Null-Byte, kaputtes YAML, gemischte Zeilenenden
/// und WikiLinks ohne brauchbares Ziel. Jede Ausnahme hier kostet eine ganze Datei im Index.
/// </summary>
public sealed class MarkdigParserEdgeCasesTests
{
    /// <summary>
    /// Obergrenze für den Hänger-Nachweis in
    /// <see cref="Parse_OnHugeInput_CompletesWithoutStalling"/>. Weit über der
    /// reinen Rechenzeit: Sie soll einen Stillstand von einer belegten Maschine trennen,
    /// nicht die Geschwindigkeit des Parsers festschreiben.
    /// </summary>
    private static readonly TimeSpan HugeInputStallLimit = TimeSpan.FromSeconds(30);

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
        // U+FEFF (BOM) am Anfang; Markdig sollte das tolerieren, der Body-Header bleibt erkennbar.
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

        // Der Inhalt landet ungekürzt im gerenderten HTML.
        string html = GzipHelper.Decompress(result.RenderedHtmlGz);
        Assert.Contains("aaaaa", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_OnSurrogatePairs_PreservesUnicodeContent()
    {
        // Emoji (in UTF-16 je ein Surrogatpaar), chinesische Schriftzeichen und mathematische
        // Operatoren aus der Grundebene.
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
        // Das Steuerzeichen NUL kommt in Textdateien kaum vor, darf den Parser aber nicht aufhalten.
        string withNul = "# Titel\n\nText mit \0 in der Mitte.";

        ParseResult result = _sut.Parse(withNul);

        // Ob das NUL im HTML erhalten bleibt, ist offen; der Text um das Zeichen herum muss ankommen.
        string html = GzipHelper.Decompress(result.RenderedHtmlGz);
        Assert.Contains("Text mit", html, StringComparison.Ordinal);
        Assert.Contains("in der Mitte.", html, StringComparison.Ordinal);
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

        // Kaputtes YAML kostet nur das Frontmatter: Es wird ganz verworfen, der Rumpf gerendert.
        ParseResult result = _sut.Parse(Markdown);

        Assert.Empty(result.Frontmatter);
        string html = GzipHelper.Decompress(result.RenderedHtmlGz);
        Assert.Contains("Body bleibt parsebar", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void Parse_OnHugeInput_CompletesWithoutStalling()
    {
        // 100.000 Überschriften, rund 1,5 Millionen Zeichen.
        StringBuilder builder = new(5 * 1024 * 1024);
        for (int i = 0; i < 100_000; i++)
        {
            _ = builder.Append("# Header ").Append(i).Append('\n');
        }
        string huge = builder.ToString();

        Stopwatch stopwatch = Stopwatch.StartNew();
        ParseResult result = _sut.Parse(huge);
        stopwatch.Stop();

        // Der Test misst Wanduhrzeit, und die hängt davon ab, wie viele Testprozesse
        // sich gerade dieselben Kerne teilen. Ein knappes Budget prüft deshalb nicht
        // den Parser, sondern die Auslastung der Maschine, und fällt genau dann um,
        // wenn ohnehin alles läuft. Zugesichert wird hier darum das, was unabhängig
        // von der Last gilt: Der Parser kommt bei dieser Größe zum Ende und liefert ein
        // vollständiges Ergebnis, statt in einem quadratischen Pfad stehen zu bleiben.
        Assert.NotNull(result);
        Assert.False(result.RenderedHtmlGz.IsEmpty);
        Assert.True(
            stopwatch.Elapsed < HugeInputStallLimit,
            $"Huge-Input-Parse benötigte {stopwatch.Elapsed.TotalSeconds:F2}s und "
                + $"überschritt damit die Stillstandsgrenze von {HugeInputStallLimit.TotalSeconds:F0}s.");
    }

    [Fact]
    public void Parse_OnRepeatedEmphasisMarkers_DoesNotCrash()
    {
        // 200 offene Hervorhebungszeichen ohne Gegenstück. Wie Markdig sie paart, ist seine
        // Sache; scheitern darf er daran nicht.
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
        // 1.500 verschachtelte Klammerpaare. Der Markdig-Renderer hat eine Verschachtelungsgrenze:
        // Er rendert entweder durch oder wirft eine ArgumentException mit "depth limit exceeded",
        // die der ParseOrchestrator je Datei abfängt. Eine andere Ausnahme fängt er dort nicht.
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
            ParseResult result = _sut.Parse(deep);
            Assert.Contains("text", GzipHelper.Decompress(result.RenderedHtmlGz), StringComparison.Ordinal);
        }
        catch (ArgumentException ex)
        {
            Assert.Contains("depth", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Parse_OnInputWithMixedLineEndings_NormalizesAndParses()
    {
        // \r, \r\n, \n in einem Dokument: Markdig nimmt alle drei als Zeilenende,
        // und die Schlagwort-Suche darf keines davon übersehen.
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

    /// <summary>
    /// Ein Verweis, dessen Ziel keinen Slug hergibt, darf das Dokument nicht kosten.
    /// </summary>
    /// <remarks>
    /// In einer Datei steht <c>[[…]]</c>, drei Punkte als Auslassungszeichen, ein Satzzeichen.
    /// Verlangte der Renderer dafür einen Slug und bekäme eine Ausnahme, verwürfe der
    /// Orchestrator die ganze Datei. Sie fehlte im Index, ohne dass jemand sie vermisst.
    /// Der Verweis führt nirgendwohin; das ist richtig. Der Text bleibt lesbar: das ist der Punkt.
    /// </remarks>
    [Theory]
    [InlineData("[[…]]")]
    [InlineData("[[+]]")]
    [InlineData("[[...]]")]
    [InlineData("[[ ]]")]
    public void Parse_OnWikiLinkWithoutSluggableTarget_KeepsTheDocument(string link)
    {
        ParseResult result = _sut.Parse("# Titel\n\nDavor. " + link + " Danach.");

        string html = GzipHelper.Decompress(result.RenderedHtmlGz);
        Assert.Contains("Davor.", html, StringComparison.Ordinal);
        Assert.Contains("Danach.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("mdexplorer://\"", html, StringComparison.Ordinal);
        Assert.Empty(result.OutlinkSlugs);
    }

    /// <remarks>
    /// Die Gegenprobe zum Fall darüber: Steht neben dem unbrauchbaren Verweis ein brauchbarer,
    /// bleibt dieser ein Verweis. Ein Ausfall darf nicht auf den Nachbarn übergreifen.
    /// </remarks>
    [Fact]
    public void Parse_OnBrokenAndGoodWikiLink_KeepsTheGoodOne()
    {
        ParseResult result = _sut.Parse("[[…]] und [[Zweites Dokument]]");

        string html = GzipHelper.Decompress(result.RenderedHtmlGz);
        Assert.Contains("mdexplorer://zweites-dokument", html, StringComparison.Ordinal);
        Assert.Equal("zweites-dokument", Assert.Single(result.OutlinkSlugs));
    }
}
