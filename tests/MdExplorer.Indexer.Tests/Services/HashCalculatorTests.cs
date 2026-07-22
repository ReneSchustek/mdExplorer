using MdExplorer.Indexer.Services;
using MdExplorer.Indexer.Tests.Fakes;

namespace MdExplorer.Indexer.Tests.Services;

public sealed class HashCalculatorTests
{
    private static readonly DateTime FixedWrite = new(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ComputeAsync_OnIdenticalContent_ReturnsIdenticalHash()
    {
        FakeFileSystem fs = new();
        fs.AddFile(@"C:\a.md", "Inhalt", FixedWrite);
        fs.AddFile(@"C:\b.md", "Inhalt", FixedWrite);
        HashCalculator sut = new(fs);

        string hashA = await sut.ComputeAsync(@"C:\a.md", CancellationToken.None).ConfigureAwait(true);
        string hashB = await sut.ComputeAsync(@"C:\b.md", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(hashA, hashB);
        Assert.Equal(64, hashA.Length);
    }

    [Fact]
    public async Task ComputeAsync_OnDifferentContent_ReturnsDifferentHash()
    {
        FakeFileSystem fs = new();
        fs.AddFile(@"C:\a.md", "Inhalt A", FixedWrite);
        fs.AddFile(@"C:\b.md", "Inhalt B", FixedWrite);
        HashCalculator sut = new(fs);

        string hashA = await sut.ComputeAsync(@"C:\a.md", CancellationToken.None).ConfigureAwait(true);
        string hashB = await sut.ComputeAsync(@"C:\b.md", CancellationToken.None).ConfigureAwait(true);

        Assert.NotEqual(hashA, hashB);
    }

    [Fact]
    public async Task ComputeAsync_OnKnownContent_ReturnsExpectedSha256()
    {
        FakeFileSystem fs = new();
        fs.AddFile(@"C:\test.md", "abc", FixedWrite);
        HashCalculator sut = new(fs);

        string hash = await sut.ComputeAsync(@"C:\test.md", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
    }

    [Fact]
    public async Task ComputeAsync_OnCanceledToken_Throws()
    {
        FakeFileSystem fs = new();
        fs.AddFile(@"C:\a.md", new string('x', 64 * 1024), FixedWrite);
        HashCalculator sut = new(fs);

        using CancellationTokenSource cts = new();
        await cts.CancelAsync().ConfigureAwait(true);

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => sut.ComputeAsync(@"C:\a.md", cts.Token)).ConfigureAwait(true);
    }
}
