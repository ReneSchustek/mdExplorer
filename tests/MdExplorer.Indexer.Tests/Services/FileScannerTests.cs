using MdExplorer.Indexer.Options;
using MdExplorer.Indexer.Services;
using MdExplorer.Indexer.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.Indexer.Tests.Services;

/// <summary>
/// Der Scanner findet nur Markdown-Dateien, lässt ausgeschlossene Ordner weg und kommt bei
/// wechselseitigen Verknüpfungen zum Ende. Folgte er einer Junction-Schleife, hinge der Erst-Scan
/// endlos; folgte er einem Link nach außen, landeten fremde Dateien im Index.
/// </summary>
public sealed class FileScannerTests
{
    private static readonly DateTime FixedWrite = new(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc);
    private const string Root = @"C:\Wurzel";

    [Fact]
    public void EnumerateMarkdownFiles_OnExistingDirectory_FindsAllMarkdownFiles()
    {
        FakeFileSystem fs = new();
        fs.AddDirectory(Root);
        fs.AddFile(@$"{Root}\a.md", "# A", FixedWrite);
        fs.AddFile(@$"{Root}\sub\b.md", "# B", FixedWrite);
        fs.AddFile(@$"{Root}\sub\ignored.txt", "x", FixedWrite);

        FileScanner sut = NewScanner(fs, new PassThroughExclusionFilter());

        List<string> result = [.. sut.EnumerateMarkdownFiles(Root, TestContext.Current.CancellationToken).Select(file => file.Path)];

        Assert.Equal(2, result.Count);
        Assert.Contains(@$"{Root}\a.md", result);
        Assert.Contains(@$"{Root}\sub\b.md", result);
    }

    [Fact]
    public void EnumerateMarkdownFiles_OnDirectoryWithExclusions_SkipsExcludedFolders()
    {
        FakeFileSystem fs = new();
        fs.AddDirectory(Root);
        fs.AddFile(@$"{Root}\readme.md", "# OK", FixedWrite);
        fs.AddFile(@$"{Root}\node_modules\dep.md", "# Skip", FixedWrite);
        fs.AddFile(@$"{Root}\.git\hooks\note.md", "# Skip", FixedWrite);
        fs.AddFile(@$"{Root}\bin\Debug\drop.md", "# Skip", FixedWrite);

        FileScanner sut = NewScanner(fs, new StubExclusionFilter(["node_modules", ".git", "bin"]));

        List<string> result = [.. sut.EnumerateMarkdownFiles(Root, TestContext.Current.CancellationToken).Select(file => file.Path)];

        _ = Assert.Single(result);
        Assert.Contains(@$"{Root}\readme.md", result);
    }

    [Fact]
    public void EnumerateMarkdownFiles_OnNonExistingRoot_ReturnsEmpty()
    {
        FakeFileSystem fs = new();
        FileScanner sut = NewScanner(fs, new PassThroughExclusionFilter());

        List<string> result = [.. sut.EnumerateMarkdownFiles(Root, TestContext.Current.CancellationToken).Select(file => file.Path)];

        Assert.Empty(result);
    }

    [Fact]
    public async Task EnumerateMarkdownFiles_OnCanceledToken_Throws()
    {
        FakeFileSystem fs = new();
        fs.AddDirectory(Root);
        fs.AddFile(@$"{Root}\a.md", "# A", FixedWrite);
        fs.AddFile(@$"{Root}\b.md", "# B", FixedWrite);
        FileScanner sut = NewScanner(fs, new PassThroughExclusionFilter());

        using CancellationTokenSource cts = new();
        await cts.CancelAsync().ConfigureAwait(true);

        _ = Assert.Throws<OperationCanceledException>(() => sut.EnumerateMarkdownFiles(Root, cts.Token).ToList());
    }

    [Fact]
    public void EnumerateMarkdownFiles_OnReciprocalSymlinkLoop_TerminatesAndYieldsEachFileOnce()
    {
        FakeFileSystem fs = new();
        fs.AddDirectory(Root);
        fs.AddDirectory(@$"{Root}\dir1");
        fs.AddDirectory(@$"{Root}\dir2");
        fs.AddFile(@$"{Root}\dir1\file1.md", "# 1", FixedWrite);
        fs.AddFile(@$"{Root}\dir2\file2.md", "# 2", FixedWrite);
        // Wechselseitige Junctions: Eine rekursive Aufzählung, die Links folgt, liefe hier endlos im Kreis.
        fs.AddSymlink(@$"{Root}\dir1\loop", @$"{Root}\dir2");
        fs.AddSymlink(@$"{Root}\dir2\loop", @$"{Root}\dir1");

        FileScanner sut = NewScanner(fs, new PassThroughExclusionFilter(), followSymlinks: false);

        List<string> result = [.. sut.EnumerateMarkdownFiles(Root, TestContext.Current.CancellationToken).Select(file => file.Path)];

        Assert.Equal(2, result.Count);
        Assert.Contains(@$"{Root}\dir1\file1.md", result);
        Assert.Contains(@$"{Root}\dir2\file2.md", result);
    }

    [Fact]
    public void EnumerateMarkdownFiles_OnReciprocalSymlinkLoop_WithFollowSymlinks_DetectsCycleAndStillTerminates()
    {
        FakeFileSystem fs = new();
        fs.AddDirectory(Root);
        fs.AddDirectory(@$"{Root}\dir1");
        fs.AddDirectory(@$"{Root}\dir2");
        fs.AddFile(@$"{Root}\dir1\file1.md", "# 1", FixedWrite);
        fs.AddFile(@$"{Root}\dir2\file2.md", "# 2", FixedWrite);
        fs.AddSymlink(@$"{Root}\dir1\loop", @$"{Root}\dir2");
        fs.AddSymlink(@$"{Root}\dir2\loop", @$"{Root}\dir1");

        FileScanner sut = NewScanner(fs, new PassThroughExclusionFilter(), followSymlinks: true);

        List<string> result = [.. sut.EnumerateMarkdownFiles(Root, TestContext.Current.CancellationToken).Select(file => file.Path)];

        // Keine Datei doppelt, Zyklus über Besucher-Set unterbrochen.
        Assert.Equal(2, result.Count);
        Assert.Contains(@$"{Root}\dir1\file1.md", result);
        Assert.Contains(@$"{Root}\dir2\file2.md", result);
    }

    [Fact]
    public void EnumerateMarkdownFiles_OnSymlinkInsideRoot_WithFollowSymlinksFalse_SkipsTarget()
    {
        FakeFileSystem fs = new();
        fs.AddDirectory(Root);
        fs.AddDirectory(@"C:\Anderswo");
        fs.AddFile(@"C:\Anderswo\extern.md", "# Extern", FixedWrite);
        // Das Linkziel liegt außerhalb der Wurzel und bleibt in der Voreinstellung draußen.
        fs.AddSymlink(@$"{Root}\link", @"C:\Anderswo");

        FileScanner sut = NewScanner(fs, new PassThroughExclusionFilter(), followSymlinks: false);

        List<string> result = [.. sut.EnumerateMarkdownFiles(Root, TestContext.Current.CancellationToken).Select(file => file.Path)];

        Assert.Empty(result);
    }

    [Fact]
    public void EnumerateMarkdownFiles_OnAFileSymlink_WithFollowSymlinksFalse_SkipsIt()
    {
        // Auch eine einzelne Datei kann eine Verknüpfung auf ein Ziel außerhalb der Wurzel sein.
        // In der Voreinstellung bleibt ihr Inhalt draußen, wie der eines verknüpften Ordners.
        FakeFileSystem fs = new();
        fs.AddDirectory(Root);
        fs.AddFile(@$"{Root}\normal.md", "# Normal", FixedWrite);
        fs.AddFile(@$"{Root}\link.md", "# Fremd", FixedWrite);
        fs.MarkFileAsReparsePoint(@$"{Root}\link.md", linkTarget: @"C:\Anderswo\fremd.md");

        FileScanner sut = NewScanner(fs, new PassThroughExclusionFilter(), followSymlinks: false);

        List<string> result = [.. sut.EnumerateMarkdownFiles(Root, TestContext.Current.CancellationToken).Select(file => file.Path)];

        Assert.Equal([@$"{Root}\normal.md"], result);
    }

    [Fact]
    public void EnumerateMarkdownFiles_OnAReparsePointFileWithoutLinkTarget_IncludesIt()
    {
        // OneDrive legt jede Datei als Reparse-Point ab, auch die vor Ort liegenden. Würde jeder
        // Reparse-Point übersprungen, fehlten in einem OneDrive-Ordner alle Notizen.
        FakeFileSystem fs = new();
        fs.AddDirectory(Root);
        fs.AddFile(@$"{Root}\cloud.md", "# Cloud", FixedWrite);
        fs.MarkFileAsReparsePoint(@$"{Root}\cloud.md");

        FileScanner sut = NewScanner(fs, new PassThroughExclusionFilter(), followSymlinks: false);

        Assert.Equal([@$"{Root}\cloud.md"], sut.EnumerateMarkdownFiles(Root, TestContext.Current.CancellationToken).Select(file => file.Path));
    }

    [Fact]
    public void EnumerateMarkdownFiles_OnAFileSymlink_WithFollowSymlinksTrue_IncludesIt()
    {
        FakeFileSystem fs = new();
        fs.AddDirectory(Root);
        fs.AddFile(@$"{Root}\link.md", "# Fremd", FixedWrite);
        fs.MarkFileAsReparsePoint(@$"{Root}\link.md", linkTarget: @"C:\Anderswo\fremd.md");

        FileScanner sut = NewScanner(fs, new PassThroughExclusionFilter(), followSymlinks: true);

        _ = Assert.Single(sut.EnumerateMarkdownFiles(Root, TestContext.Current.CancellationToken));
    }

    private static FileScanner NewScanner(
        FakeFileSystem fs,
        Indexer.Abstractions.IExclusionFilter exclusionFilter,
        bool followSymlinks = false)
    {
        IndexerOptions options = new() { FollowSymlinks = followSymlinks };
        return new FileScanner(fs, exclusionFilter, options.ToOptions(), NullLogger<FileScanner>.Instance);
    }
}
