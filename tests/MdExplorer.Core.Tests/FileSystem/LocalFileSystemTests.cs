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
        string path = Path.Combine(_testRoot, "größe.bin");
        byte[] payload = new byte[123];
        File.WriteAllBytes(path, payload);

        long size = _sut.GetFileSize(path);

        Assert.Equal(123, size);
    }

    [Fact]
    public void ReadAllBytes_OnExistingFile_ReadsContent()
    {
        string path = Path.Combine(_testRoot, "sync.md");
        File.WriteAllText(path, "Synchron");

        byte[] bytes = _sut.ReadAllBytes(path);

        Assert.Equal("Synchron", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task OpenRead_OnExistingFile_ReturnsReadableStream()
    {
        string path = Path.Combine(_testRoot, "stream.md");
        await File.WriteAllTextAsync(path, "Streamed", CancellationToken.None).ConfigureAwait(true);

        await using Stream stream = _sut.OpenRead(path);
        using StreamReader reader = new(stream);
        string content = await reader.ReadToEndAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("Streamed", content);
    }

    [Fact]
    public void GetLastWriteTimeUtc_OnExistingFile_ReturnsUtcTimestamp()
    {
        string path = Path.Combine(_testRoot, "zeit.md");
        File.WriteAllText(path, "x");
        DateTime stamp = new(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);

        DateTime result = _sut.GetLastWriteTimeUtc(path);

        Assert.Equal(stamp, result);
    }

    [Fact]
    public void EnumerateDirectories_ReturnsImmediateSubdirectories()
    {
        _ = Directory.CreateDirectory(Path.Combine(_testRoot, "sub1"));
        _ = Directory.CreateDirectory(Path.Combine(_testRoot, "sub2"));
        _ = Directory.CreateDirectory(Path.Combine(_testRoot, "sub1", "tief"));

        IEnumerable<string> result = _sut.EnumerateDirectories(_testRoot);

        Assert.Equal(2, result.Count()); // nur direkte Unterordner, nicht "tief"
    }

    [Fact]
    public void IsReparsePoint_OnNormalDirectory_ReturnsFalse()
    {
        Assert.False(_sut.IsReparsePoint(_testRoot));
    }

    [Fact]
    public void IsReparsePoint_OnMissingPath_ReturnsFalse()
    {
        Assert.False(_sut.IsReparsePoint(Path.Combine(_testRoot, "gibt-es-nicht")));
    }

    [Fact]
    public void GetDirectoryFinalPath_OnNormalDirectory_ReturnsFullPath()
    {
        string result = _sut.GetDirectoryFinalPath(_testRoot);

        Assert.Equal(Path.GetFullPath(_testRoot), result);
    }
    /// <remarks>
    /// Verschieben und Löschen sind die beiden Vorgänge, die etwas kaputt machen können.
    /// Sie standen bis zum 16.08.2026 ohne einen einzigen Test da — bemerkt beim Nachrechnen
    /// der Abdeckung, nicht durch einen Fehler. Das ist der unangenehmere der beiden Wege,
    /// auf denen so etwas auffällt.
    /// </remarks>
    [Fact]
    public void MoveFile_MovesTheContentAndLeavesNothingBehind()
    {
        string quelle = Path.Combine(_testRoot, "quelle.md");
        string ziel = Path.Combine(_testRoot, "ziel.md");
        File.WriteAllText(quelle, "Inhalt");

        _sut.MoveFile(quelle, ziel);

        Assert.False(File.Exists(quelle));
        Assert.Equal("Inhalt", File.ReadAllText(ziel));
    }

    /// <remarks>
    /// Fehlt das Zielverzeichnis, wird es angelegt. Sonst müsste jeder Aufrufer daran denken —
    /// und einer denkt nicht daran.
    /// </remarks>
    [Fact]
    public void MoveFile_IntoAMissingDirectory_CreatesTheDirectory()
    {
        string quelle = Path.Combine(_testRoot, "quelle.md");
        string ziel = Path.Combine(_testRoot, "neu", "tiefer", "ziel.md");
        File.WriteAllText(quelle, "Inhalt");

        _sut.MoveFile(quelle, ziel);

        Assert.Equal("Inhalt", File.ReadAllText(ziel));
    }

    /// <remarks>
    /// Der Punkt, an dem <c>overwrite: false</c> hängt: Ein vorhandenes Ziel muss den Vorgang
    /// abbrechen. Andernfalls verschluckt ein Umbenennen eine fremde Datei — und beides,
    /// Quelle und Ziel, ist danach nicht mehr da, wo es hingehört.
    /// </remarks>
    [Fact]
    public void MoveFile_OntoAnExistingTarget_ThrowsAndKeepsBothFiles()
    {
        string quelle = Path.Combine(_testRoot, "quelle.md");
        string ziel = Path.Combine(_testRoot, "belegt.md");
        File.WriteAllText(quelle, "neu");
        File.WriteAllText(ziel, "alt");

        _ = Assert.Throws<IOException>(() => _sut.MoveFile(quelle, ziel));

        Assert.Equal("neu", File.ReadAllText(quelle));
        Assert.Equal("alt", File.ReadAllText(ziel));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MoveFile_OnBlankPath_Throws(string leer)
    {
        string ziel = Path.Combine(_testRoot, "ziel.md");

        _ = Assert.Throws<ArgumentException>(() => _sut.MoveFile(leer, ziel));
        _ = Assert.Throws<ArgumentException>(() => _sut.MoveFile(ziel, leer));
    }

    [Fact]
    public void DeleteFile_OnExistingFile_RemovesIt()
    {
        string pfad = Path.Combine(_testRoot, "weg.md");
        File.WriteAllText(pfad, "Inhalt");

        _sut.DeleteFile(pfad);

        Assert.False(File.Exists(pfad));
    }

    /// <remarks>
    /// Wer löschen will, was schon weg ist, hat sein Ziel erreicht. Diese Zusage steht als
    /// Kommentar im Code; hier steht sie als Prüfung — sonst hinge der Aufrufer an einer
    /// Ausnahme für einen Zustand, den er sich gewünscht hat.
    /// </remarks>
    [Fact]
    public void DeleteFile_OnAlreadyMissingFile_StaysQuiet()
    {
        _sut.DeleteFile(Path.Combine(_testRoot, "gab-es-nie.md"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DeleteFile_OnBlankPath_Throws(string leer)
    {
        _ = Assert.Throws<ArgumentException>(() => _sut.DeleteFile(leer));
    }
}
