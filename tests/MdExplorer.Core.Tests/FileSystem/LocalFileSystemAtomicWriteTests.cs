using System.Text;
using MdExplorer.Core.FileSystem;

namespace MdExplorer.Core.Tests.FileSystem;

/// <summary>
/// Gespeichert wird über eine Zwischendatei im selben Ordner, die danach an die Stelle des
/// Ziels rückt. Ein abgebrochenes Speichern hinterlässt so weder eine halbe Notiz noch Reste,
/// und der Inhalt kommt Byte für Byte an, ohne vorangestellte Byte-Order-Mark.
/// </summary>
/// <remarks>
/// Dazu gehört <see cref="LocalFileSystem.GetDirectoryFinalPath"/>: Wo die Auflösung eines
/// Ordners scheitert, gilt der Ausgangspfad statt einer Ausnahme.
/// </remarks>
public sealed class LocalFileSystemAtomicWriteTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalFileSystem _sut;

    public LocalFileSystemAtomicWriteTests()
    {
        _testRoot = Directory.CreateTempSubdirectory("MdExplorer.Tests-").FullName;
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

        await _sut.WriteAllBytesAtomicAsync(path, payload, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(File.Exists(path));
        Assert.Equal(payload, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Fact]
    public async Task WriteAllBytesAtomicAsync_OnExistingFile_OverwritesContent()
    {
        string path = Path.Combine(_testRoot, "exists.md");
        await File.WriteAllTextAsync(path, "alt", TestContext.Current.CancellationToken).ConfigureAwait(true);

        byte[] payload = Encoding.UTF8.GetBytes("neu");
        await _sut.WriteAllBytesAtomicAsync(path, payload, TestContext.Current.CancellationToken).ConfigureAwait(true);

        string result = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal("neu", result);
    }

    [Fact]
    public async Task WriteAllBytesAtomicAsync_LeavesNoTempFile()
    {
        string path = Path.Combine(_testRoot, "clean.md");
        byte[] payload = Encoding.UTF8.GetBytes("inhalt");

        await _sut.WriteAllBytesAtomicAsync(path, payload, TestContext.Current.CancellationToken).ConfigureAwait(true);

        string[] tempFiles = Directory.GetFiles(_testRoot, ".*.tmp");
        Assert.Empty(tempFiles);
    }

    [Fact]
    public async Task WriteAllBytesAtomicAsync_DoesNotWriteBom()
    {
        string path = Path.Combine(_testRoot, "nobom.md");
        byte[] payload = Encoding.UTF8.GetBytes("ascii");

        await _sut.WriteAllBytesAtomicAsync(path, payload, TestContext.Current.CancellationToken).ConfigureAwait(true);

        byte[] disk = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.False(disk.Length >= 3 && disk[0] == 0xEF && disk[1] == 0xBB && disk[2] == 0xBF);
    }

    [Fact]
    public async Task WriteAllBytesAtomicAsync_WhenTheMoveFails_RemovesTheTemporaryFile()
    {
        // Der Zielpfad ist ein Verzeichnis; das abschließende Verschieben scheitert. Bliebe
        // die Zwischendatei liegen, sammelten sich bei jedem Fehlversuch Reste im Ordner an.
        string path = Path.Combine(_testRoot, "blockiert.md");
        _ = Directory.CreateDirectory(path);
        byte[] payload = Encoding.UTF8.GetBytes("inhalt");

        // Windows meldet den blockierten Zielpfad je nach Zustand als E/A- oder als
        // Zugriffsfehler: für den Test zählt nur, dass er nicht verschluckt wird.
        _ = await Assert.ThrowsAnyAsync<SystemException>(
            () => _sut.WriteAllBytesAtomicAsync(path, payload, TestContext.Current.CancellationToken)).ConfigureAwait(true);

        Assert.Empty(Directory.GetFiles(_testRoot, ".*.tmp"));
    }

    [Fact]
    public async Task WriteAllBytesAtomicAsync_WithoutADirectoryInThePath_Throws()
    {
        // Der Aufruf muss scheitern, statt relativ zum Arbeitsverzeichnis zu schreiben.
        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.WriteAllBytesAtomicAsync("relativ.md", Encoding.UTF8.GetBytes("x"), TestContext.Current.CancellationToken))
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task WriteAllBytesAtomicAsync_WhenCancelled_KeepsTheOldFileAndLeavesNoTempFile()
    {
        string path = Path.Combine(_testRoot, "notiz.md");
        await File.WriteAllTextAsync(path, "alt", TestContext.Current.CancellationToken).ConfigureAwait(true);
        using CancellationTokenSource cts = new();
        await cts.CancelAsync().ConfigureAwait(true);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _sut.WriteAllBytesAtomicAsync(path, Encoding.UTF8.GetBytes("neu"), cts.Token)).ConfigureAwait(true);

        Assert.Equal("alt", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal([path], Directory.GetFiles(_testRoot));
    }

    [Fact]
    public void WriteAllBytesAtomic_OnExistingFile_ReplacesItAndLeavesNoTempFile()
    {
        string directory = Path.Combine(_testRoot, "neu-angelegt");
        string path = Path.Combine(directory, "layout.json");
        _sut.WriteAllBytesAtomic(path, Encoding.UTF8.GetBytes("alt"));

        _sut.WriteAllBytesAtomic(path, Encoding.UTF8.GetBytes("neu"));

        Assert.Equal("neu", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(directory, ".*.tmp"));
    }

    /// <remarks>
    /// Scheitert das abschließende Umbenennen, bleibt der alte Inhalt, wie er war, und keine
    /// Zwischendatei liegt herum. Das ist die Zusage des atomaren Schreibens: ganz oder gar nicht.
    /// </remarks>
    [Fact]
    public void WriteAllBytesAtomic_WhenTheMoveFails_KeepsTheOldContentAndRemovesTheTemporaryFile()
    {
        string path = Path.Combine(_testRoot, "gesperrt.json");
        File.WriteAllText(path, "alt");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            _ = Assert.ThrowsAny<SystemException>(() => _sut.WriteAllBytesAtomic(path, Encoding.UTF8.GetBytes("neu")));
        }

        Assert.Equal("alt", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_testRoot, ".*.tmp"));
    }

    [Fact]
    public void GetDirectoryFinalPath_OnAPlainDirectory_ReturnsTheFullPath()
    {
        string outcome = _sut.GetDirectoryFinalPath(_testRoot);

        Assert.Equal(Path.GetFullPath(_testRoot), outcome, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDirectoryFinalPath_OnAFilePath_FallsBackToTheOriginalPath()
    {
        // Eine Datei ist kein Verzeichnis. Die Auflösung scheitert und muss auf den
        // Ausgangspfad zurückfallen statt zu werfen.
        string storedFile = Path.Combine(_testRoot, "keine-mappe.md");
        await File.WriteAllTextAsync(storedFile, "x", TestContext.Current.CancellationToken).ConfigureAwait(true);

        string outcome = _sut.GetDirectoryFinalPath(storedFile);

        Assert.Equal(Path.GetFullPath(storedFile), outcome, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetDirectoryFinalPath_OnAMissingDirectory_FallsBackToTheOriginalPath()
    {
        string absentPath = Path.Combine(_testRoot, "gibt-es-nicht");

        string outcome = _sut.GetDirectoryFinalPath(absentPath);

        Assert.Equal(Path.GetFullPath(absentPath), outcome, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetDirectoryFinalPath_WithoutAPath_Throws()
    {
        _ = Assert.Throws<ArgumentException>(() => _sut.GetDirectoryFinalPath("   "));
    }
}
