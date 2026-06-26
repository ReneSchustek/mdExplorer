using MdExplorer.Core.Abstractions;
using MdExplorer.Core.FileSystem;

namespace MdExplorer.Core.Tests.FileSystem;

public sealed class LocalFileSystemTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalFileSystem _sut;

    public LocalFileSystemTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "MdExplorer.Tests", Guid.NewGuid().ToString("N"));
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
    public void DirectoryExists_OnExistingPath_ReturnsTrue()
    {
        Assert.True(_sut.DirectoryExists(_testRoot));
    }

    [Fact]
    public void FileExists_OnMissingFile_ReturnsFalse()
    {
        string path = Path.Combine(_testRoot, "nichtda.txt");
        Assert.False(_sut.FileExists(path));
    }

    [Fact]
    public void EnsureDirectoryExists_OnMissingPath_CreatesIt()
    {
        string nested = Path.Combine(_testRoot, "neu", "verschachtelt");
        _sut.EnsureDirectoryExists(nested);
        Assert.True(Directory.Exists(nested));
    }

    [Fact]
    public void EnumerateFiles_OnDirectoryWithMarkdownFiles_FindsThem()
    {
        File.WriteAllText(Path.Combine(_testRoot, "a.md"), "# A");
        File.WriteAllText(Path.Combine(_testRoot, "b.md"), "# B");
        File.WriteAllText(Path.Combine(_testRoot, "ignored.txt"), "x");

        IEnumerable<string> result = _sut.EnumerateFiles(_testRoot, "*.md", recursive: false);

        Assert.Equal(2, result.Count());
    }

    [Fact]
    public async Task ReadAllBytesAsync_OnExistingFile_ReadsContent()
    {
        string path = Path.Combine(_testRoot, "datei.md");
        await File.WriteAllTextAsync(path, "Hallo Welt", CancellationToken.None).ConfigureAwait(true);

        byte[] bytes = await _sut.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("Hallo Welt", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void GetFileSize_OnExistingFile_ReturnsCorrectLength()
    {
        string path = Path.Combine(_testRoot, "groesse.bin");
        byte[] payload = new byte[123];
        File.WriteAllBytes(path, payload);

        long size = _sut.GetFileSize(path);

        Assert.Equal(123, size);
    }
}
