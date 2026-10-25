using System.Buffers.Binary;
using System.IO;
using System.Text;
using MdExplorer.App.Hosting;

namespace MdExplorer.App.Tests.Hosting;

/// <summary>
/// Was zwischen zwei Instanzen läuft, kommt von einem anderen Prozess. Eine falsche Länge oder
/// ein abgebrochener Auftrag wird abgewiesen, statt beliebig viel Speicher anzufordern.
/// </summary>
public sealed class InstanceMessageCodecTests
{
    [Theory]
    [InlineData(@"C:\Notizen\Plan.md")]
    [InlineData(@"D:\Größe und Maß\Übersicht – 2026.md")]
    [InlineData(@"\\server\freigabe\notiz.markdown")]
    public async Task Request_RoundTrips(string path)
    {
        using MemoryStream stream = new();

        await InstanceMessageCodec.WriteRequestAsync(stream, path, TestContext.Current.CancellationToken).ConfigureAwait(true);
        stream.Position = 0;
        string? read = await InstanceMessageCodec.ReadRequestAsync(stream, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(path, read, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Request_WithoutAPath_RoundTripsAsWindowOnly()
    {
        using MemoryStream stream = new();

        await InstanceMessageCodec.WriteRequestAsync(stream, absolutePath: null, TestContext.Current.CancellationToken).ConfigureAwait(true);
        stream.Position = 0;

        Assert.Equal(4, stream.Length);
        Assert.Null(await InstanceMessageCodec.ReadRequestAsync(stream, TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(InstanceMessageCodec.MaxPayloadBytes + 1)]
    [InlineData(int.MaxValue)]
    public async Task ReadRequest_WithAnImpossibleLength_Rejects(int length)
    {
        using MemoryStream stream = new(Header(length));

        _ = await Assert.ThrowsAsync<InvalidDataException>(
            () => InstanceMessageCodec.ReadRequestAsync(stream, TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    [Fact]
    public async Task ReadRequest_WhenTheStreamEndsEarly_Rejects()
    {
        byte[] message = [.. Header(10), .. "C:\\a"u8.ToArray()];
        using MemoryStream stream = new(message);

        _ = await Assert.ThrowsAsync<EndOfStreamException>(
            () => InstanceMessageCodec.ReadRequestAsync(stream, TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    [Fact]
    public async Task ReadRequest_WithInvalidUtf8_Rejects()
    {
        byte[] message = [.. Header(2), 0xC3, 0x28];
        using MemoryStream stream = new(message);

        _ = await Assert.ThrowsAsync<InvalidDataException>(
            () => InstanceMessageCodec.ReadRequestAsync(stream, TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    [Fact]
    public async Task WriteRequest_WithAPathBeyondTheLimit_Rejects()
    {
        using MemoryStream stream = new();
        string path = "C:\\" + new string('a', InstanceMessageCodec.MaxPayloadBytes) + ".md";

        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => InstanceMessageCodec.WriteRequestAsync(stream, path, TestContext.Current.CancellationToken)).ConfigureAwait(true);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public async Task WriteRequest_AtTheLongestWindowsPath_StaysWithinTheLimit()
    {
        // 32.767 Zeichen, jedes drei Bytes lang: das Längste, was Windows als Pfad zulässt.
        using MemoryStream stream = new();
        string path = new('€', 32_767);

        await InstanceMessageCodec.WriteRequestAsync(stream, path, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(4 + Encoding.UTF8.GetByteCount(path), stream.Length);
    }

    [Fact]
    public async Task Acknowledgement_RoundTrips()
    {
        using MemoryStream stream = new();

        await InstanceMessageCodec.WriteAcknowledgementAsync(stream, TestContext.Current.CancellationToken).ConfigureAwait(true);
        stream.Position = 0;

        await InstanceMessageCodec.ReadAcknowledgementAsync(stream, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(1, stream.Position);
    }

    [Fact]
    public async Task ReadAcknowledgement_WithAnotherByte_Rejects()
    {
        using MemoryStream stream = new([0x15]);

        _ = await Assert.ThrowsAsync<InvalidDataException>(
            () => InstanceMessageCodec.ReadAcknowledgementAsync(stream, TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    [Fact]
    public async Task ReadAcknowledgement_WhenTheOtherSideHangsUp_Rejects()
    {
        using MemoryStream stream = new();

        _ = await Assert.ThrowsAsync<EndOfStreamException>(
            () => InstanceMessageCodec.ReadAcknowledgementAsync(stream, TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    private static byte[] Header(int length)
    {
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);
        return header;
    }
}
