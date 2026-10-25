using System.Text;
using MdExplorer.Core.Text;

namespace MdExplorer.Core.Tests.Text;

/// <summary>
/// Eine Byte-Order-Mark wird abgeschnitten, und nur sie. Bliebe sie stehen, begänne der Text mit
/// einem unsichtbaren Zeichen; schnitte der Decoder zu gierig, verlöre er echte Bytes.
/// </summary>
public sealed class Utf8DecoderTests
{
    [Fact]
    public void DecodeNoBom_WithoutBom_ReturnsString()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("hallo welt");

        string result = Utf8Decoder.DecodeNoBom(bytes);

        Assert.Equal("hallo welt", result);
    }

    [Fact]
    public void DecodeNoBom_WithBom_StripsBom()
    {
        byte[] bytes = [.. Encoding.UTF8.Preamble.ToArray(), .. Encoding.UTF8.GetBytes("hallo welt")];

        string result = Utf8Decoder.DecodeNoBom(bytes);

        Assert.Equal("hallo welt", result);
    }

    [Fact]
    public void DecodeNoBom_EmptyArray_ReturnsEmpty()
    {
        string result = Utf8Decoder.DecodeNoBom([]);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void DecodeNoBom_OnlyBom_ReturnsEmpty()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF];

        string result = Utf8Decoder.DecodeNoBom(bytes);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void DecodeNoBom_NonBomPrefix_KeepsTheLetterBehindIt()
    {
        // EF BB ohne das dritte BOM-Byte ist kein BOM. Abgeschnitten wird nichts; das „A“
        // dahinter muss ankommen, die zwei unvollständigen Bytes werden zu Ersatzzeichen.
        byte[] bytes = [0xEF, 0xBB, 0x41];

        string result = Utf8Decoder.DecodeNoBom(bytes);

        Assert.EndsWith("A", result, StringComparison.Ordinal);
        Assert.Contains('�', result);
    }

    [Fact]
    public void TryDecodeStrict_OnValidUtf8_ReturnsTheTextWithoutBom()
    {
        byte[] bytes = [.. Encoding.UTF8.Preamble.ToArray(), .. Encoding.UTF8.GetBytes("Grüße")];

        bool valid = Utf8Decoder.TryDecodeStrict(bytes, out string text);

        Assert.True(valid);
        Assert.Equal("Grüße", text);
    }

    [Fact]
    public void TryDecodeStrict_OnWindows1252_ReportsItAndStillYieldsADisplayText()
    {
        // 0xE4 ist „ä“ in Windows-1252 und als einzelnes Byte kein gültiges UTF-8. Die verlust-
        // behaftete Dekodierung meldete das nicht; der Editor speicherte danach U+FFFD zurück.
        byte[] bytes = [0x4B, 0xE4, 0x73, 0x65];

        bool valid = Utf8Decoder.TryDecodeStrict(bytes, out string text);

        Assert.False(valid);
        Assert.Equal("K�se", text);
    }

    [Fact]
    public void DecodeNoBom_Null_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => Utf8Decoder.DecodeNoBom(null!));
    }
}
