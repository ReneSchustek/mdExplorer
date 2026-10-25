using MdExplorer.Core.Abstractions;
using MdExplorer.Core.FileSystem;

namespace MdExplorer.Core.Tests.FileSystem;

/// <summary>
/// Das echte Dateisystem in einem Wegwerfordner unter Temp. Die Attrappen der übrigen Tests
/// bilden dieses Verhalten nach; weicht es hier ab, prüfen jene gegen eine falsche Vorlage.
/// </summary>
public sealed class LocalFileSystemTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalFileSystem _sut;

    public LocalFileSystemTests()
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
    public void EnumerateFileEntries_OnAFile_CarriesSizeTimeAndNoReparseMark()
    {
        string path = Path.Combine(_testRoot, "a.md");
        File.WriteAllText(path, "12345");
        DateTime written = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);
        File.WriteAllText(Path.Combine(_testRoot, "ignored.txt"), "x");

        FileEntry entry = Assert.Single(_sut.EnumerateFileEntries(_testRoot, "*.md"));

        Assert.Equal(path, entry.Path);
        Assert.Equal(5, entry.SizeBytes);
        Assert.Equal(written, entry.LastWriteTimeUtc);
        Assert.False(entry.IsReparsePoint);
    }

    [Fact]
    public async Task ReadAllBytesAsync_OnExistingFile_ReadsContent()
    {
        string path = Path.Combine(_testRoot, "datei.md");
        await File.WriteAllTextAsync(path, "Hallo Welt", TestContext.Current.CancellationToken).ConfigureAwait(true);

        byte[] bytes = await _sut.ReadAllBytesAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true);

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
        await File.WriteAllTextAsync(path, "Streamed", TestContext.Current.CancellationToken).ConfigureAwait(true);

        await using Stream stream = _sut.OpenRead(path);
        using StreamReader reader = new(stream);
        string content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

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
    public void IsReparsePoint_OnAJunction_ReturnsTrue()
    {
        // Eine Verzeichnisverbindung braucht anders als ein symbolischer Link weder Adminrechte
        // noch den Entwicklermodus; .NET selbst bietet zum Anlegen keine Schnittstelle.
        string target = Path.Combine(_testRoot, "ziel");
        string junction = Path.Combine(_testRoot, "verbindung");
        _ = Directory.CreateDirectory(target);
        CreateJunction(junction, target);
        bool junctionIsReparsePoint;
        try
        {
            junctionIsReparsePoint = _sut.IsReparsePoint(junction);
        }
        finally
        {
            // Nur die Verbindung selbst entfernen; das rekursive Aufräumen scheitert an ihr.
            Directory.Delete(junction);
        }

        Assert.True(junctionIsReparsePoint);
        Assert.False(_sut.IsReparsePoint(target));
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
    /// Ohne eigenen Test fiele ein Fehler darin erst auf, wenn eine Notiz verloren ist.
    /// </remarks>
    [Fact]
    public void MoveFile_MovesTheContentAndLeavesNothingBehind()
    {
        string source = Path.Combine(_testRoot, "quelle.md");
        string destination = Path.Combine(_testRoot, "ziel.md");
        File.WriteAllText(source, "Inhalt");

        _sut.MoveFile(source, destination);

        Assert.False(File.Exists(source));
        Assert.Equal("Inhalt", File.ReadAllText(destination));
    }

    /// <remarks>
    /// Fehlt das Zielverzeichnis, wird es angelegt. Sonst müsste jeder Aufrufer daran denken,
    /// und einer denkt nicht daran.
    /// </remarks>
    [Fact]
    public void MoveFile_IntoAMissingDirectory_CreatesTheDirectory()
    {
        string source = Path.Combine(_testRoot, "quelle.md");
        string destination = Path.Combine(_testRoot, "neu", "tiefer", "ziel.md");
        File.WriteAllText(source, "Inhalt");

        _sut.MoveFile(source, destination);

        Assert.Equal("Inhalt", File.ReadAllText(destination));
    }

    /// <remarks>
    /// Ein vorhandenes Ziel bricht den Vorgang ab, dafür steht <c>overwrite: false</c>.
    /// Andernfalls verschluckte ein Umbenennen eine fremde Datei.
    /// </remarks>
    [Fact]
    public void MoveFile_OntoAnExistingTarget_ThrowsAndKeepsBothFiles()
    {
        string source = Path.Combine(_testRoot, "quelle.md");
        string destination = Path.Combine(_testRoot, "belegt.md");
        File.WriteAllText(source, "neu");
        File.WriteAllText(destination, "alt");

        _ = Assert.Throws<IOException>(() => _sut.MoveFile(source, destination));

        Assert.Equal("neu", File.ReadAllText(source));
        Assert.Equal("alt", File.ReadAllText(destination));
    }

    /// <remarks>
    /// Unter Windows sind „notiz.md“ und „Notiz.md“ dieselbe Datei. Das Umbenennen darf trotzdem
    /// nicht als belegtes Ziel scheitern, sonst ließe sich die Schreibweise nie ändern.
    /// </remarks>
    [Fact]
    public void MoveFile_ChangingOnlyTheCase_RenamesTheFile()
    {
        string source = Path.Combine(_testRoot, "notiz.md");
        File.WriteAllText(source, "inhalt");

        _sut.MoveFile(source, Path.Combine(_testRoot, "Notiz.md"));

        Assert.Equal(["Notiz.md"], Directory.GetFiles(_testRoot).Select(Path.GetFileName));
        Assert.Equal("inhalt", File.ReadAllText(Path.Combine(_testRoot, "Notiz.md")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MoveFile_OnBlankPath_Throws(string empty)
    {
        string destination = Path.Combine(_testRoot, "ziel.md");

        _ = Assert.Throws<ArgumentException>(() => _sut.MoveFile(empty, destination));
        _ = Assert.Throws<ArgumentException>(() => _sut.MoveFile(destination, empty));
    }

    [Fact]
    public void DeleteFile_OnExistingFile_RemovesIt()
    {
        string filePath = Path.Combine(_testRoot, "weg.md");
        File.WriteAllText(filePath, "Inhalt");

        _sut.DeleteFile(filePath);

        Assert.False(File.Exists(filePath));
    }

    /// <remarks>
    /// Wer löschen will, was schon weg ist, hat sein Ziel erreicht. Sonst hinge der Aufrufer
    /// an einer Ausnahme für einen Zustand, den er sich gewünscht hat.
    /// </remarks>
    [Fact]
    public void DeleteFile_OnAlreadyMissingFile_StaysQuiet()
    {
        _sut.DeleteFile(Path.Combine(_testRoot, "gab-es-nie.md"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DeleteFile_OnBlankPath_Throws(string empty)
    {
        _ = Assert.Throws<ArgumentException>(() => _sut.DeleteFile(empty));
    }

    private static void CreateJunction(string junction, string target)
    {
        System.Diagnostics.ProcessStartInfo start = new("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(junction);
        start.ArgumentList.Add(target);
        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException("cmd.exe ließ sich nicht starten.");
        _ = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
