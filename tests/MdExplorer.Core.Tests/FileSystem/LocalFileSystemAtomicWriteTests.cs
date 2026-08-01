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
    public async Task WriteAllBytesAtomicAsync_OnNewFile_PersistsTheContent()
    {
        string path = Path.Combine(_testRoot, "neu.md");
        byte[] payload = Encoding.UTF8.GetBytes("# Hallo\r\nText");

        await _sut.WriteAllBytesAtomicAsync(path, payload, CancellationToken.None).ConfigureAwait(true);

        Assert.True(File.Exists(path));
        Assert.Equal(payload, await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task WriteAllBytesAtomicAsync_OnExistingFile_OverwritesContent()
    {
        string path = Path.Combine(_testRoot, "exists.md");
        await File.WriteAllTextAsync(path, "alt", CancellationToken.None).ConfigureAwait(true);

        byte[] payload = Encoding.UTF8.GetBytes("neu");
        await _sut.WriteAllBytesAtomicAsync(path, payload, CancellationToken.None).ConfigureAwait(true);

        string result = await File.ReadAllTextAsync(path, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal("neu", result);
    }

    [Fact]
    public async Task WriteAllBytesAtomicAsync_LeavesNoTempFile()
    {
        string path = Path.Combine(_testRoot, "clean.md");
        byte[] payload = Encoding.UTF8.GetBytes("inhalt");

        await _sut.WriteAllBytesAtomicAsync(path, payload, CancellationToken.None).ConfigureAwait(true);

        string[] tempFiles = Directory.GetFiles(_testRoot, ".*.tmp");
        Assert.Empty(tempFiles);
    }

    [Fact]
    public async Task WriteAllBytesAtomicAsync_DoesNotWriteBom()
    {
        string path = Path.Combine(_testRoot, "nobom.md");
        byte[] payload = Encoding.UTF8.GetBytes("ascii");

        await _sut.WriteAllBytesAtomicAsync(path, payload, CancellationToken.None).ConfigureAwait(true);

        byte[] disk = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);
        Assert.False(disk.Length >= 3 && disk[0] == 0xEF && disk[1] == 0xBB && disk[2] == 0xBF);
    }

    [Fact]
    public async Task WriteAllBytesAtomicAsync_WhenTheMoveFails_RemovesTheTemporaryFile()
    {
        // Der Zielpfad ist ein Verzeichnis — das abschließende Verschieben scheitert. Bliebe
        // die Zwischendatei liegen, sammelten sich bei jedem Fehlversuch Reste im Ordner an.
        string path = Path.Combine(_testRoot, "blockiert.md");
        _ = Directory.CreateDirectory(path);
        byte[] payload = Encoding.UTF8.GetBytes("inhalt");

        // Windows meldet den blockierten Zielpfad je nach Zustand als E/A- oder als
        // Zugriffsfehler — für den Test zählt nur, dass er nicht verschluckt wird.
        _ = await Assert.ThrowsAnyAsync<SystemException>(
            () => _sut.WriteAllBytesAtomicAsync(path, payload, CancellationToken.None)).ConfigureAwait(true);

        Assert.Empty(Directory.GetFiles(_testRoot, ".*.tmp"));
    }

    [Fact]
    public async Task WriteAllBytesAtomicAsync_WithoutADirectoryInThePath_Throws()
    {
        _ = await Assert.ThrowsAnyAsync<Exception>(
            () => _sut.WriteAllBytesAtomicAsync("relativ.md", Encoding.UTF8.GetBytes("x"), CancellationToken.None))
            .ConfigureAwait(true);
    }

    [Fact]
    public void GetDirectoryFinalPath_OnAPlainDirectory_ReturnsTheFullPath()
    {
        string ergebnis = _sut.GetDirectoryFinalPath(_testRoot);

        Assert.Equal(Path.GetFullPath(_testRoot), ergebnis, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDirectoryFinalPath_OnAFilePath_FallsBackToTheOriginalPath()
    {
        // Eine Datei ist kein Verzeichnis — die Auflösung scheitert und muss auf den
        // Ausgangspfad zurückfallen statt zu werfen.
        string datei = Path.Combine(_testRoot, "keine-mappe.md");
        await File.WriteAllTextAsync(datei, "x", CancellationToken.None).ConfigureAwait(true);

        string ergebnis = _sut.GetDirectoryFinalPath(datei);

        Assert.Equal(Path.GetFullPath(datei), ergebnis, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetDirectoryFinalPath_OnAMissingDirectory_FallsBackToTheOriginalPath()
    {
        string fehlt = Path.Combine(_testRoot, "gibt-es-nicht");

        string ergebnis = _sut.GetDirectoryFinalPath(fehlt);

        Assert.Equal(Path.GetFullPath(fehlt), ergebnis, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetDirectoryFinalPath_WithoutAPath_Throws()
    {
        _ = Assert.Throws<ArgumentException>(() => _sut.GetDirectoryFinalPath("   "));
    }
}
