using System.Text;
using MdExplorer.Parser.Services;
using MdExplorer.Parser.Tests.Helpers;

namespace MdExplorer.Parser.Tests.Services;

/// <summary>
/// Das Frontmatter einer Notiz kommt aus Dateien, die der Nutzer nicht alle selbst geschrieben
/// hat, etwa aus einem fremden Repository in einer indizierten Wurzel. Ein paar hundert Byte YAML
/// durften bisher den Prozess beenden: über Aliase, die sich gegenseitig vervielfachen, oder über
/// eine Verschachtelung, an der der YAML-Leser seinen Aufrufstapel überlaufen lässt. Beides gilt
/// jetzt als kaputtes Frontmatter; das Dokument selbst bleibt im Index.
/// </summary>
public sealed class FrontmatterLimitsTests
{
    private readonly FrontmatterExtractor _sut = new();

    [Fact]
    public void Extract_WithAnAliasChain_DropsTheFrontmatter()
    {
        // Sechs Ebenen mit je zehn Verweisen auf die vorige: 3 Millionen Zeichen aus 379 Byte.
        IReadOnlyDictionary<string, string> result = _sut.Extract(TestPipelineFactory.Parse(AliasChain(levels: 6)));

        Assert.Empty(result);
    }

    [Fact]
    public void Extract_WithASingleAlias_DropsTheFrontmatter()
    {
        const string Markdown = """
            ---
            a: &shared wert
            b: *shared
            ---
            Text
            """;

        IReadOnlyDictionary<string, string> result = _sut.Extract(TestPipelineFactory.Parse(Markdown));

        Assert.Empty(result);
    }

    [Fact]
    public void Extract_WithAnAnchorButNoAlias_KeepsTheFrontmatter()
    {
        const string Markdown = """
            ---
            a: &shared wert
            ---
            Text
            """;

        IReadOnlyDictionary<string, string> result = _sut.Extract(TestPipelineFactory.Parse(Markdown));

        Assert.Equal("wert", result["a"], StringComparer.Ordinal);
    }

    [Fact]
    public void Extract_NestedDeeperThanTheLimit_DropsTheFrontmatter()
    {
        IReadOnlyDictionary<string, string> result = _sut.Extract(TestPipelineFactory.Parse(Nested(depth: 200)));

        Assert.Empty(result);
    }

    [Fact]
    public void Extract_NestedFarBeyondTheStackDepth_DropsTheFrontmatterWithoutCrashing()
    {
        // 30.000 Ebenen liegen unter der Größengrenze und weit über der Tiefe, bei der das Laden
        // in Knoten den Aufrufstapel überlaufen ließ. Vor der Prüfung beendete das den Testhost.
        IReadOnlyDictionary<string, string> result = _sut.Extract(TestPipelineFactory.Parse(Nested(depth: 30_000)));

        Assert.Empty(result);
    }

    [Fact]
    public void Extract_WhenTheFrontmatterIsDropped_LogsTheReason()
    {
        RecordingLogger<FrontmatterExtractor> logger = new();
        FrontmatterExtractor sut = new(logger);

        _ = sut.Extract(TestPipelineFactory.Parse(AliasChain(levels: 1)));

        RecordedLogEntry entry = Assert.Single(logger.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, entry.Level);
        Assert.Contains("Alias", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Extract_NestedWithinTheLimit_KeepsTheValue()
    {
        IReadOnlyDictionary<string, string> result = _sut.Extract(TestPipelineFactory.Parse(Nested(depth: 3, innermost: "a")));

        Assert.Equal("a", result["k"], StringComparer.Ordinal);
    }

    [Fact]
    public void Extract_OverTheSizeLimit_DropsTheFrontmatter()
    {
        string markdown = "---\nk: " + new string('x', FrontmatterExtractor.MaxFrontmatterLength) + "\n---\nText\n";

        IReadOnlyDictionary<string, string> result = _sut.Extract(TestPipelineFactory.Parse(markdown));

        Assert.Empty(result);
    }

    [Fact]
    public void Extract_JustBelowTheSizeLimit_KeepsTheValue()
    {
        // "k: " und der Zeilenumbruch zählen mit; der Wert füllt den Rest bis an die Grenze.
        int valueLength = FrontmatterExtractor.MaxFrontmatterLength - "k: \n".Length;
        string markdown = "---\nk: " + new string('x', valueLength) + "\n---\nText\n";

        IReadOnlyDictionary<string, string> result = _sut.Extract(TestPipelineFactory.Parse(markdown));

        Assert.Equal(valueLength, result["k"].Length);
    }

    private static string AliasChain(int levels)
    {
        StringBuilder yaml = new("---\na0: &a0 [x]\n");
        for (int level = 1; level <= levels; level++)
        {
            _ = yaml.Append(System.Globalization.CultureInfo.InvariantCulture, $"a{level}: &a{level} [");
            _ = yaml.AppendJoin(", ", Enumerable.Repeat($"*a{level - 1}", 10));
            _ = yaml.Append("]\n");
        }

        return yaml.Append("---\nText\n").ToString();
    }

    private static string Nested(int depth, string innermost = "") =>
        "---\nk: " + new string('[', depth) + innermost + new string(']', depth) + "\n---\nText\n";
}
