using MdExplorer.Core.Text;

namespace MdExplorer.Core.Tests.Text;

/// <summary>
/// Tests für <see cref="AlphabetIndex"/>.
/// </summary>
public sealed class AlphabetIndexTests
{
    [Theory]
    [InlineData("Architektur", 'A')]
    [InlineData("zeitplan", 'Z')]
    [InlineData("  Mit Leerzeichen davor", 'M')]
    public void LetterOf_OnLatinLetter_ReturnsUppercase(string sortKey, char expected)
    {
        Assert.Equal(expected, AlphabetIndex.LetterOf(sortKey));
    }

    [Theory]
    [InlineData("Änderungen", 'A')]
    [InlineData("Öffnungszeiten", 'O')]
    [InlineData("Übersicht", 'U')]
    [InlineData("ärger", 'A')]
    [InlineData("éclair", 'E')]
    public void LetterOf_OnDiacritics_FallsBackToBaseLetter(string sortKey, char expected)
    {
        Assert.Equal(expected, AlphabetIndex.LetterOf(sortKey));
    }

    [Fact]
    public void LetterOf_OnSharpS_ReturnsS()
    {
        // „ß" lässt sich nicht in Grundbuchstabe plus Zeichen zerlegen und braucht deshalb
        // eine eigene Zuordnung — sonst landete es unter „Sonstiges".
        Assert.Equal('S', AlphabetIndex.LetterOf("ßonderfall"));
    }

    [Theory]
    [InlineData("2026-Planung")]
    [InlineData("_intern")]
    [InlineData("#tag")]
    [InlineData("日本語")]
    public void LetterOf_OnNonLatinStart_ReturnsOtherLetter(string sortKey)
    {
        Assert.Equal(AlphabetIndex.OtherLetter, AlphabetIndex.LetterOf(sortKey));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void LetterOf_OnMissingKey_ReturnsOtherLetter(string? sortKey)
    {
        Assert.Equal(AlphabetIndex.OtherLetter, AlphabetIndex.LetterOf(sortKey));
    }

    [Fact]
    public void Letters_CoverTheAlphabetAndTheCollector()
    {
        Assert.Equal(27, AlphabetIndex.Letters.Count);
        Assert.Equal('A', AlphabetIndex.Letters[0]);
        Assert.Equal('Z', AlphabetIndex.Letters[25]);
        Assert.Equal(AlphabetIndex.OtherLetter, AlphabetIndex.Letters[26]);
    }

    /// <remarks>
    /// Die Leiste ändert ihre Breite nie: Sie führt immer alle Buchstaben, auch die ohne
    /// Einträge. Eine Leiste, die je nach Bestand anders aussieht, ist kein verlässlicher
    /// Anlaufpunkt — deshalb hält dieser Test die feste Länge fest.
    /// </remarks>
    [Fact]
    public void Letters_AreStableRegardlessOfContent()
    {
        Assert.All(
            AlphabetIndex.Letters.Take(AlphabetIndex.Letters.Count - 1),
            letter => Assert.True(letter is >= 'A' and <= 'Z'));
    }
}
