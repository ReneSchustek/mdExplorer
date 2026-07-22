using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Core.Settings;
using MdExplorer.Indexer.Services;
using MdExplorer.Indexer.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.Indexer.Tests.Services;

public sealed class GlobExclusionFilterTests
{
    private const string Root = @"C:\Wurzel";

    [Fact]
    public void IsExcluded_OnNodeModulesPattern_ExcludesMatchingFile()
    {
        using GlobExclusionFilter sut = Build(["**/node_modules/**"]);

        bool excluded = sut.IsExcluded(@"C:\Wurzel\node_modules\dep.md", Root);

        Assert.True(excluded);
    }

    [Fact]
    public void IsExcluded_OnPatternMissingMatch_DoesNotExclude()
    {
        using GlobExclusionFilter sut = Build(["**/node_modules/**"]);

        bool excluded = sut.IsExcluded(@"C:\Wurzel\readme.md", Root);

        Assert.False(excluded);
    }

    [Fact]
    public void IsExcluded_OnNegationOverridingPreviousExclude_DoesNotExclude()
    {
        using GlobExclusionFilter sut = Build(["*.md", "!important.md"]);

        bool excluded = sut.IsExcluded(@"C:\Wurzel\important.md", Root);

        Assert.False(excluded);
    }

    [Fact]
    public void IsExcluded_OnSiblingDirectorySharingPrefix_DoesNotApplyRootPatterns()
    {
        using GlobExclusionFilter sut = Build(["**/*.md"]);

        bool excluded = sut.IsExcluded(@"C:\Wurzel-evil\evil.md", Root);

        Assert.False(excluded);
    }

    [Fact]
    public void Invalidate_OnSettingsChange_RebuildsMatcher()
    {
        FakeSettingsService settings = BuildSettings([]);
        using GlobExclusionFilter sut = Build(settings);

        Assert.False(sut.IsExcluded(@"C:\Wurzel\sub\file.md", Root));

        AppSettings updated = settings.Current with
        {
            Indexing = new IndexingSettings(settings.Current.Indexing.Roots, ["**/sub/**"], [], true),
        };
        _ = settings.SaveAsync(updated, CancellationToken.None);

        Assert.True(sut.IsExcluded(@"C:\Wurzel\sub\file.md", Root));
    }

    [Fact]
    public void IsExcluded_OnUiExcludedFolder_ExcludesFilesUnderThatFolder()
    {
        FakeSettingsService settings = BuildSettings([], [@"C:\Wurzel\paused"]);
        using GlobExclusionFilter sut = Build(settings);

        Assert.True(sut.IsExcluded(@"C:\Wurzel\paused\note.md", Root));
        Assert.True(sut.IsExcluded(@"C:\Wurzel\paused\deep\note.md", Root));
        Assert.False(sut.IsExcluded(@"C:\Wurzel\active\note.md", Root));
    }

    [Fact]
    public void IsExcluded_OnUiExcludedFolderSharingPrefix_DoesNotMisclassifySibling()
    {
        FakeSettingsService settings = BuildSettings([], [@"C:\Wurzel\paused"]);
        using GlobExclusionFilter sut = Build(settings);

        // "paused-extra" startet zwar mit demselben Praefix, ist aber ein eigenstaendiger Ordner.
        Assert.False(sut.IsExcluded(@"C:\Wurzel\paused-extra\note.md", Root));
    }

    [Fact]
    public void IsExcluded_AfterUiExcludedFolderRemovedFromSettings_PicksUpChange()
    {
        FakeSettingsService settings = BuildSettings([], [@"C:\Wurzel\paused"]);
        using GlobExclusionFilter sut = Build(settings);

        Assert.True(sut.IsExcluded(@"C:\Wurzel\paused\note.md", Root));

        AppSettings updated = settings.Current with
        {
            Indexing = new IndexingSettings(settings.Current.Indexing.Roots, [], [], true),
        };
        _ = settings.SaveAsync(updated, CancellationToken.None);

        Assert.False(sut.IsExcluded(@"C:\Wurzel\paused\note.md", Root));
    }

    private static GlobExclusionFilter Build(IReadOnlyList<string> patterns)
    {
        FakeSettingsService settings = BuildSettings(patterns);
        return Build(settings);
    }

    private static GlobExclusionFilter Build(FakeSettingsService settings)
    {
        MdIgnoreHierarchy hierarchy = new(new MdIgnoreReader(new EmptyFs()));
        return new GlobExclusionFilter(settings, hierarchy, NullLogger<GlobExclusionFilter>.Instance);
    }

    private static FakeSettingsService BuildSettings(IReadOnlyList<string> patterns)
        => BuildSettings(patterns, []);

    private static FakeSettingsService BuildSettings(
        IReadOnlyList<string> patterns,
        IReadOnlyList<string> uiExcludedFolders)
    {
        AppSettings initial = new(
            AppSettings.CurrentSchemaVersion,
            new IndexingSettings([Root], patterns, uiExcludedFolders, true),
            AppearanceSettings.Default,
            BehaviorSettings.Default);
        return new FakeSettingsService(initial);
    }

    private sealed class EmptyFs : IFileSystem
    {
        public bool DirectoryExists(string path) => true;
        public bool FileExists(string path) => false;
        public void EnsureDirectoryExists(string path) { }
        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive) => [];
        public IEnumerable<string> EnumerateDirectories(string directory) => [];
        public bool IsReparsePoint(string path) => false;
        public string GetDirectoryFinalPath(string path) => path;
        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) => Task.FromResult(Array.Empty<byte>());
        public byte[] ReadAllBytes(string path) => [];
        public System.IO.Stream OpenRead(string path) => System.IO.Stream.Null;
        public DateTime GetLastWriteTimeUtc(string path) => DateTime.UnixEpoch;
        public long GetFileSize(string path) => 0;
        public Task WriteAllBytesAtomicAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
