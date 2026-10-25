using MdExplorer.Search.Services;

namespace MdExplorer.Search.Tests.Services;

/// <summary>
/// Aus einem regulären Ausdruck dürfen nur Wortanfänge in den Vorfilter, die in jedem Treffer an
/// einer Wortgrenze stehen. Alles andere ließe Treffer still fehlen: <c>ment</c> trifft „Dokument“,
/// FTS5 fände über <c>"ment"*</c> das Dokument nie.
/// </summary>
public sealed class RegexRequiredLiteralsTests
{
    [Theory]
    [InlineData(@"\bTODO\b", "TODO")]
    [InlineData(@"e-mail", "mail")]
    [InlineData(@"^Titel: \w+", "Titel")]
    [InlineData(@"\bRechnung\s+\d+", "Rechnung")]
    [InlineData(@"\bcolou?r", "colo")]
    [InlineData(@"\bab+c", "ab")]
    [InlineData(@"\bAPI\d{3}\s+Main\b", "API|Main")]
    [InlineData(@"Bericht \bAnhang", "Anhang")]
    public void Extract_ReturnsTheGuaranteedWordStarts(string pattern, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        Assert.Equal(expected.Split('|'), RegexRequiredLiterals.Extract(pattern));
    }

    [Theory]
    [InlineData("ment")] // kann mitten im Wort stehen
    [InlineData("Rechnung")] // ebenso: „Abrechnung“
    [InlineData(@"colou?r")]
    [InlineData("TODO|FIXME")] // keiner der beiden steht in jedem Treffer
    [InlineData(@"\b(foo|bar)baz")]
    [InlineData(@"[abc]def")]
    [InlineData(@"\ba*bc")] // ohne das a steht "bc" mitten im Wort
    [InlineData(".+")]
    [InlineData("(?x) foo bar")] // Leerzeichen zählen in diesem Modus nicht
    [InlineData(@"\bx\b")] // ein Buchstabe ist kein brauchbarer Vorfilter
    public void Extract_WithoutAGuaranteedWordStart_ReturnsNothing(string pattern)
    {
        Assert.Empty(RegexRequiredLiterals.Extract(pattern));
    }
}
