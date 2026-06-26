using MdExplorer.Indexer.Options;
using MdExplorer.Indexer.Services;
using MdExplorer.Indexer.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.Indexer.Tests.Services;

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

        List<string> result = [.. sut.EnumerateMarkdownFiles(Root, CancellationToken.None)];

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

        List<string> result = [.. sut.EnumerateMarkdownFiles(Root, CancellationToken.None)];

        _ = Assert.Single(result);
        Assert.Contains(@$"{Root}\readme.md", result);
    }

    [Fact]
    public void EnumerateMarkdownFiles_OnNonExistingRoot_ReturnsEmpty()
    {
        FakeFileSystem fs = new();
        FileScanner sut = NewScanner(fs, new PassThroughExclusionFilter());

        List<string> result = [.. sut.EnumerateMarkdownFiles(Root, CancellationToken.None)];

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
        // Wechselseitige Junctions — würde die Default-Recursive-Enumeration in eine Endlosschleife treiben.
        fs.AddSymlink(@$"{Root}\dir1\loop", @$"{Root}\dir2");
        fs.AddSymlink(@$"{Root}\dir2\loop", @$"{Root}\dir1");

        FileScanner sut = NewScanner(fs, new PassThroughExclusionFilter(), followSymlinks: false);

        List<string> result = [.. sut.EnumerateMarkdownFiles(Root, CancellationToken.None)];

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

        List<string> result = [.. sut.EnumerateMarkdownFiles(Root, CancellationToken.None)];

        // Keine Datei doppelt — Zyklus über Besucher-Set unterbrochen.
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
        // Linkziel liegt ausserhalb des Roots — soll bei Default-Setting nicht indiziert werden.
        fs.AddSymlink(@$"{Root}\link", @"C:\Anderswo");

        FileScanner sut = NewScanner(fs, new PassThroughExclusionFilter(), followSymlinks: false);

        List<string> result = [.. sut.EnumerateMarkdownFiles(Root, CancellationToken.None)];

        Assert.Empty(result);
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
