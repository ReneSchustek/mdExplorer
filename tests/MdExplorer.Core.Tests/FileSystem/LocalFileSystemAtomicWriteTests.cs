using System.Text;
using MdExplorer.Core.FileSystem;

namespace MdExplorer.Core.Tests.FileSystem;

public sealed class LocalFileSystemAtomicWriteTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalFileSystem _sut;

    public LocalFileSystemAtomicWriteTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "MdExplorer.AtomicWriteTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_testRoot);
        _sut = new LocalFileSystem();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAllBytesAtomicAsync_OnNewFile_PersistiertInhalt()
    {
        string path = Path.Combine(_testRoot, "neu.md");
        byte[] payload = Encoding.UTF8.GetBytes("# Hallo\r\nText");

        await _sut.WriteAllBytesAtomicAsync(path, payload, CancellationToken.None).ConfigureAwait(true);

        Assert.True(File.Exists(path));
        Assert.Equal(payload, await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task WriteAllBytesAtomicAsync_OnExistingFile_UeberschreibtInhalt()
    {
        string path = Path.Combine(_testRoot, "exists.md");
        await File.WriteAllTextAsync(path, "alt", CancellationToken.None).ConfigureAwait(true);

        byte[] payload = Encoding.UTF8.GetBytes("neu");
        await _sut.WriteAllBytesAtomicAsync(path, payload, CancellationToken.None).ConfigureAwait(true);

        string result = await File.ReadAllTextAsync(path, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal("neu", result);
    }

    [Fact]
    public async Task WriteAllBytesAtomicAsync_HinterlaesstKeineTempDatei()
    {
        string path = Path.Combine(_testRoot, "clean.md");
        byte[] payload = Encoding.UTF8.GetBytes("inhalt");

        await _sut.WriteAllBytesAtomicAsync(path, payload, CancellationToken.None).ConfigureAwait(true);

        string[] tempFiles = Directory.GetFiles(_testRoot, ".*.tmp");
        Assert.Empty(tempFiles);
    }

    [Fact]
    public async Task WriteAllBytesAtomicAsync_BomVerwendetNicht()
    {
        string path = Path.Combine(_testRoot, "nobom.md");
        byte[] payload = Encoding.UTF8.GetBytes("ascii");

        await _sut.WriteAllBytesAtomicAsync(path, payload, CancellationToken.None).ConfigureAwait(true);

        byte[] disk = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);
        Assert.False(disk.Length >= 3 && disk[0] == 0xEF && disk[1] == 0xBB && disk[2] == 0xBF);
    }
}
