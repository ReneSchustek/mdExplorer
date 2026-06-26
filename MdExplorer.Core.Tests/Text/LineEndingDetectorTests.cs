using MdExplorer.Core.Text;

namespace MdExplorer.Core.Tests.Text;

public sealed class LineEndingDetectorTests
{
    [Fact]
    public void Detect_CrlfText_ReturnsCrlf()
    {
        LineEndingStyle result = LineEndingDetector.Detect("eins\r\nzwei\r\ndrei");
        Assert.Equal(LineEndingStyle.Crlf, result);
    }

    [Fact]
    public void Detect_LfText_ReturnsLf()
    {
        LineEndingStyle result = LineEndingDetector.Detect("eins\nzwei\ndrei");
        Assert.Equal(LineEndingStyle.Lf, result);
    }

    [Fact]
    public void Detect_CrText_ReturnsCr()
    {
        LineEndingStyle result = LineEndingDetector.Detect("eins\rzwei\rdrei");
        Assert.Equal(LineEndingStyle.Cr, result);
    }

    [Fact]
    public void Detect_OhneZeilenumbruch_ReturnsDefault()
    {
        LineEndingStyle result = LineEndingDetector.Detect("kein umbruch");
        Assert.Equal(LineEndingDetector.Default, result);
    }

    [Fact]
    public void Detect_MixedGleichstand_BevorzugtCrlf()
    {
        LineEndingStyle result = LineEndingDetector.Detect("a\r\nb\nc\r");
        Assert.Equal(LineEndingStyle.Crlf, result);
    }

    [Fact]
    public void Normalize_CrlfNachLf_TauschtAlleUmbrueche()
    {
        string normalized = LineEndingDetector.Normalize("a\r\nb\r\nc", LineEndingStyle.Lf);
        Assert.Equal("a\nb\nc", normalized);
    }

    [Fact]
    public void Normalize_LfNachCrlf_TauschtAlleUmbrueche()
    {
        string normalized = LineEndingDetector.Normalize("a\nb\nc", LineEndingStyle.Crlf);
        Assert.Equal("a\r\nb\r\nc", normalized);
    }

    [Theory]
    [InlineData(LineEndingStyle.Crlf, "\r\n")]
    [InlineData(LineEndingStyle.Lf, "\n")]
    [InlineData(LineEndingStyle.Cr, "\r")]
    public void ToToken_LiefertErwarteteTokens(LineEndingStyle style, string expected)
    {
        Assert.Equal(expected, LineEndingDetector.ToToken(style));
    }
}
