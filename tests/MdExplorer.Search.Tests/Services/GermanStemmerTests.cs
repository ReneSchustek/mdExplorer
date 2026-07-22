using MdExplorer.Search.Services;

namespace MdExplorer.Search.Tests.Services;

/// <summary>
/// Sichert die Suffix-Stripping-Regeln des deutschen Stemmers. Vollständig
/// linguistisch ist er nicht — der Test fixiert die explizit dokumentierten Regeln.
/// </summary>
public sealed class GermanStemmerTests
{
    [Theory]
    [InlineData("auto", "auto")]
    [InlineData("Autos", "auto")]
    [InlineData("Autotag", "autotag")] // keine Endung -tag im Regelsatz, bleibt unverändert
    [InlineData("laufen", "lauf")]
    [InlineData("läufer", "läuf")]
    [InlineData("Bedeutung", "bedeut")]
    [InlineData("Bedeutungen", "bedeut")]
    [InlineData("Wichtigkeit", "wichtig")]
    [InlineData("freundlich", "freund")]
    [InlineData("schmackhaft", "schmackhaft")] // keine bekannte Endung
    [InlineData("ab", "ab")] // unter MinStemLength
    [InlineData("xy", "xy")] // unter MinStemLength
    public void Stem_AppliesDocumentedSuffixRules(string input, string expected)
    {
        string actual = GermanStemmer.Stem(input);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Stem_OnNull_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => GermanStemmer.Stem(null!));
    }
}
