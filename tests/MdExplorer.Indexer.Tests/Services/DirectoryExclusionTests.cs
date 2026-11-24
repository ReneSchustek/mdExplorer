using MdExplorer.Core.Models;
using MdExplorer.Core.Settings;
using MdExplorer.Indexer.Options;
using MdExplorer.Indexer.Services;
using MdExplorer.Indexer.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.Indexer.Tests.Services;

/// <summary>
/// Ein ausgeschlossener Ordner wird gar nicht erst betreten, wie bei Git.
/// </summary>
/// <remarks>
/// Werkzeugordner wie vendor oder node_modules enthalten oft mehr Unterordner als der ganze
/// Notizbestand. Betritt der Scanner sie, kostet jeder Erst-Scan Minuten, ohne dass eine Datei daraus
/// je in den Index käme. Übersprungen werden darf aber nur, was sicher ganz ausgeschlossen ist: Ein
/// „!“ von weiter oben muss eine Datei darunter wieder aufnehmen können, so sagt es das Handbuch zu.
/// </remarks>
public sealed class DirectoryExclusionTests
{
    private const string Root = @"C:\Wurzel";
    private static readonly DateTime FixedWrite = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(@"C:\Wurzel\vendor")]
    [InlineData(@"C:\Wurzel\app\vendor")]
    [InlineData(@"C:\Wurzel\app\vendor\deep")]
    public void IsDirectoryExcluded_OnAnythingBelowPattern_ExcludesTheFolder(string folder)
    {
        using GlobExclusionFilter sut = Filter(["**/vendor/**"]);

        Assert.True(sut.IsDirectoryExcluded(folder, Root));
    }

    [Fact]
    public void IsDirectoryExcluded_OnFolderSharingTheNamePrefix_KeepsItOpen()
    {
        using GlobExclusionFilter sut = Filter(["**/vendor/**"]);

        Assert.False(sut.IsDirectoryExcluded(@"C:\Wurzel\vendors", Root));
    }

    // Diese Muster treffen Dateien, nicht ganze Ordner; was darunter liegt, entscheidet die Datei.
    [Theory]
    [InlineData("**/*.md", @"C:\Wurzel\notes")]
    [InlineData("drafts/*", @"C:\Wurzel\drafts")]
    public void IsDirectoryExcluded_OnFilePattern_KeepsTheFolderOpen(string pattern, string folder)
    {
        using GlobExclusionFilter sut = Filter([pattern]);

        Assert.False(sut.IsDirectoryExcluded(folder, Root));
    }

    [Fact]
    public void IsDirectoryExcluded_OnNegationAboveTheFolder_KeepsItOpen()
    {
        using GlobExclusionFilter sut = Filter(["archive/**", "!archive/2026/**"]);

        Assert.False(sut.IsDirectoryExcluded(@"C:\Wurzel\archive", Root));
    }

    // Wie bei Git: Eine .mdignore im ausgeschlossenen Ordner selbst nimmt nichts wieder auf.
    [Fact]
    public void IsDirectoryExcluded_OnNegationInsideTheExcludedFolder_StillExcludesIt()
    {
        using GlobExclusionFilter sut = FilterWithMdIgnore(["**/vendor/**"], @"C:\Wurzel\vendor", "!keep.md");

        Assert.True(sut.IsDirectoryExcluded(@"C:\Wurzel\vendor", Root));
    }

    [Fact]
    public void IsDirectoryExcluded_OnMdIgnorePatternAbove_ExcludesTheFolder()
    {
        using GlobExclusionFilter sut = FilterWithMdIgnore([], @"C:\Wurzel\notes", "drafts/**");

        Assert.True(sut.IsDirectoryExcluded(@"C:\Wurzel\notes\drafts", Root));
    }

    [Theory]
    [InlineData(@"C:\Wurzel\paused")]
    [InlineData(@"C:\Wurzel\paused\sub")]
    public void IsDirectoryExcluded_OnUiPausedFolder_ExcludesItAndItsChildren(string folder)
    {
        using GlobExclusionFilter sut = Filter([], [@"C:\Wurzel\paused"]);

        Assert.True(sut.IsDirectoryExcluded(folder, Root));
    }

    [Fact]
    public void IsDirectoryExcluded_OnTheRootItself_IsFalse()
    {
        using GlobExclusionFilter sut = Filter(["**/**"]);

        Assert.False(sut.IsDirectoryExcluded(Root, Root));
    }

    [Fact]
    public void EnumerateMarkdownFiles_OnExcludedFolder_DoesNotEnterIt()
    {
        FakeFileSystem fs = new();
        fs.AddDirectory(Root);
        fs.AddFile(@"C:\Wurzel\readme.md", "# OK", FixedWrite);
        fs.AddFile(@"C:\Wurzel\vendor\pkg\docs\deep.md", "# Skip", FixedWrite);
        using GlobExclusionFilter filter = Filter(["**/vendor/**"], fs: fs);

        List<string> result = Scan(fs, filter);

        Assert.Equal([@"C:\Wurzel\readme.md"], result);
        Assert.DoesNotContain(fs.EnumeratedDirectories, directory => directory.Contains(@"\vendor", StringComparison.OrdinalIgnoreCase));
    }

    // Die Zusage aus dem Handbuch: „archive/**“ mit „!archive/2026/**“ behält das Jahr 2026.
    [Fact]
    public void EnumerateMarkdownFiles_OnNegationBelowAnExcludedFolder_StillFindsTheFile()
    {
        FakeFileSystem fs = new();
        fs.AddDirectory(Root);
        fs.AddFile(@"C:\Wurzel\archive\2025\old.md", "# Alt", FixedWrite);
        fs.AddFile(@"C:\Wurzel\archive\2026\new.md", "# Neu", FixedWrite);
        using GlobExclusionFilter filter = Filter(["archive/**", "!archive/2026/**"], fs: fs);

        List<string> result = Scan(fs, filter);

        Assert.Equal([@"C:\Wurzel\archive\2026\new.md"], result);
    }

    private static List<string> Scan(FakeFileSystem fs, GlobExclusionFilter filter)
    {
        FileScanner scanner = new(fs, filter, new IndexerOptions().ToOptions(), NullLogger<FileScanner>.Instance);
        return [.. scanner.EnumerateMarkdownFiles(Root, TestContext.Current.CancellationToken).Select(file => file.Path)];
    }

    private static GlobExclusionFilter Filter(
        IReadOnlyList<string> patterns,
        IReadOnlyList<string>? uiExcludedFolders = null,
        FakeFileSystem? fs = null)
    {
        AppSettings settings = new(
            AppSettings.CurrentSchemaVersion,
            new IndexingSettings([Root], patterns, uiExcludedFolders ?? [], true),
            AppearanceSettings.Default,
            BehaviorSettings.Default);
        return new GlobExclusionFilter(
            new FakeSettingsService(settings),
            new MdIgnoreHierarchy(new MdIgnoreReader(fs ?? new FakeFileSystem())),
            NullLogger<GlobExclusionFilter>.Instance);
    }

    private static GlobExclusionFilter FilterWithMdIgnore(IReadOnlyList<string> patterns, string folder, string mdIgnoreContent)
    {
        FakeFileSystem fs = new();
        fs.AddDirectory(Root);
        fs.AddDirectory(folder);
        fs.AddFile(Path.Combine(folder, ".mdignore"), mdIgnoreContent + "\n", DateTime.UnixEpoch);
        return Filter(patterns, fs: fs);
    }
}
