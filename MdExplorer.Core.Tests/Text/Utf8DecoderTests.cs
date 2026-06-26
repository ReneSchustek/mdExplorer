using System.Text;
using MdExplorer.Core.Text;

namespace MdExplorer.Core.Tests.Text;

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
    public void DecodeNoBom_NonBomPrefix_PreservesBytes()
    {
        byte[] bytes = [0xEF, 0xBB, 0x41];

        string result = Utf8Decoder.DecodeNoBom(bytes);

        Assert.Equal(Encoding.UTF8.GetString(bytes), result);
    }

    [Fact]
    public void DecodeNoBom_Null_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => Utf8Decoder.DecodeNoBom(null!));
    }
}
