using System.IO;
using System.Text;
using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Jedes Graph-Fenster lädt sein Dokument aus einer eigenen Datei. Mit einer gemeinsamen lud ein
/// Fenster beim Neuladen den Graphen eines anderen.
/// </summary>
public sealed class GraphSnapshotFileTests
{
    private const string Directory = @"C:\Daten\MdExplorer\webview2";

    private static readonly Guid FirstWindow = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondWindow = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void TwoWindows_WriteToDifferentFiles()
    {
        FakeFileSystem fileSystem = new();

        GraphSnapshotFile first = new(fileSystem, Directory, FirstWindow);
        GraphSnapshotFile second = new(fileSystem, Directory, SecondWindow);

        Assert.NotEqual(first.FilePath, second.FilePath, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Directory, Path.GetDirectoryName(first.FilePath));
    }

    [Fact]
    public async Task WriteAsync_StoresTheDocumentWithoutByteOrderMark()
    {
        FakeFileSystem fileSystem = new();
        GraphSnapshotFile sut = new(fileSystem, Directory, FirstWindow);

        _ = await sut.WriteAsync("<p>Ä</p>", TestContext.Current.CancellationToken);

        Assert.Contains(Directory, fileSystem.Directories);
        Assert.Equal(Encoding.UTF8.GetBytes("<p>Ä</p>"), fileSystem.WrittenFiles[sut.FilePath]);
    }

    [Fact]
    public async Task WriteAsync_GivesEveryVersionItsOwnAddress()
    {
        GraphSnapshotFile sut = new(new FakeFileSystem(), Directory, FirstWindow);

        Uri first = await sut.WriteAsync("eins", TestContext.Current.CancellationToken);
        Uri second = await sut.WriteAsync("zwei", TestContext.Current.CancellationToken);

        Assert.NotEqual(first, second);
        Assert.True(sut.IsThisDocument(first.AbsoluteUri));
        Assert.True(sut.IsThisDocument(second.AbsoluteUri));
    }

    [Fact]
    public async Task IsThisDocument_RejectsTheFileOfAnotherWindow()
    {
        FakeFileSystem fileSystem = new();
        GraphSnapshotFile sut = new(fileSystem, Directory, FirstWindow);
        GraphSnapshotFile other = new(fileSystem, Directory, SecondWindow);

        Uri foreign = await other.WriteAsync("fremd", TestContext.Current.CancellationToken);

        Assert.False(sut.IsThisDocument(foreign.AbsoluteUri));
        Assert.False(sut.IsThisDocument("https://example.org/graph.html"));
    }

    [Fact]
    public async Task WriteAsync_WhenWritingFails_PassesTheErrorOn()
    {
        FakeFileSystem fileSystem = new() { FailOnWrite = new IOException("Datenträger voll") };
        GraphSnapshotFile sut = new(fileSystem, Directory, FirstWindow);

        _ = await Assert.ThrowsAsync<IOException>(() => sut.WriteAsync("x", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Delete_RemovesTheFileOfThisWindowOnly()
    {
        FakeFileSystem fileSystem = new();
        GraphSnapshotFile sut = new(fileSystem, Directory, FirstWindow);

        sut.Delete();

        Assert.Equal([sut.FilePath], fileSystem.DeletedFiles);
    }
}
