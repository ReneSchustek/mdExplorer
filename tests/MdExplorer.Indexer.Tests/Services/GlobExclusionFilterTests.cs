using MdExplorer.Core.Models;
using MdExplorer.Core.Settings;
using MdExplorer.Indexer.Services;
using MdExplorer.Indexer.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.Indexer.Tests.Services;

/// <summary>
/// Der Ausschlussfilter wendet Muster aus den Einstellungen wurzelrelativ an, Muster aus einer
/// .mdignore relativ zu ihrem Ordner, dazu die in der Oberfläche ausgeschlossenen Ordner. Er
/// beachtet Verneinungen, verwechselt keine Geschwisterordner mit gleichem Namensanfang und baut
/// sich nach einer Einstellungsänderung neu auf. Ein Fehler hier blendet Dateien still aus dem
/// Index aus oder lässt ausgeschlossene darin stehen.
/// </summary>
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
        _ = settings.SaveAsync(updated, TestContext.Current.CancellationToken);

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

        // "paused-extra" startet zwar mit demselben Präfix, ist aber ein eigenständiger Ordner.
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
        _ = settings.SaveAsync(updated, TestContext.Current.CancellationToken);

        Assert.False(sut.IsExcluded(@"C:\Wurzel\paused\note.md", Root));
    }

    [Fact]
    public void IsExcluded_OnAFileOutsideTheRoot_DoesNotExclude()
    {
        // Ein Pfad außerhalb der Wurzel erlaubt keine Aussage; die Muster sind
        // wurzelrelativ. "Nicht ausgeschlossen" ist hier die sichere Antwort.
        using GlobExclusionFilter sut = Build(["**/*.md"]);

        bool excluded = sut.IsExcluded(@"C:\a.md", @"C:\Wurzel\tief");

        Assert.False(excluded);
    }

    [Fact]
    public void IsExcluded_WithBlankPatterns_IgnoresThem()
    {
        // Leerzeilen entstehen leicht in einer handgepflegten Musterliste. Ein leeres
        // Muster darf nicht als "passt auf alles" durchgehen.
        using GlobExclusionFilter sut = Build(["   ", string.Empty, "!", "**/node_modules/**"]);

        Assert.False(sut.IsExcluded(@"C:\Wurzel\readme.md", Root));
        Assert.True(sut.IsExcluded(@"C:\Wurzel\node_modules\dep.md", Root));
    }

    [Fact]
    public void IsExcluded_WithBlankUiExcludedFolders_IgnoresThem()
    {
        FakeSettingsService settings = BuildSettings(["**/node_modules/**"], ["   ", string.Empty]);
        using GlobExclusionFilter sut = Build(settings);

        Assert.False(sut.IsExcluded(@"C:\Wurzel\readme.md", Root));
    }

    [Fact]
    public void IsExcluded_WithAnMdIgnoreInASubfolder_MatchesRelativeToThatFolder()
    {
        // Wie in einer .gitignore: "*.md" in sub/.mdignore meint die Dateien in sub.
        using GlobExclusionFilter sut = BuildWithMdIgnore([], @"C:\Wurzel\sub", "*.md");

        Assert.True(sut.IsExcluded(@"C:\Wurzel\sub\note.md", Root));
    }

    [Fact]
    public void IsExcluded_WithAnMdIgnoreInASubfolder_DoesNotReadItsPatternsFromTheRoot()
    {
        // Wurzelrelativ gelesen träfe "sub/note.md" die Datei; vom eigenen Ordner aus gibt es
        // kein sub/sub/note.md.
        using GlobExclusionFilter sut = BuildWithMdIgnore([], @"C:\Wurzel\sub", "sub/note.md");

        Assert.False(sut.IsExcluded(@"C:\Wurzel\sub\note.md", Root));
    }

    [Fact]
    public void IsExcluded_WithANegationInASubfolder_KeepsTheFileDespiteARootPattern()
    {
        using GlobExclusionFilter sut = BuildWithMdIgnore(["**/*.md"], @"C:\Wurzel\sub", "!keep.md");

        Assert.False(sut.IsExcluded(@"C:\Wurzel\sub\keep.md", Root));
        Assert.True(sut.IsExcluded(@"C:\Wurzel\sub\other.md", Root));
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        GlobExclusionFilter sut = Build(["**/node_modules/**"]);

        sut.Dispose();
        sut.Dispose();
    }

    private static GlobExclusionFilter Build(IReadOnlyList<string> patterns)
    {
        FakeSettingsService settings = BuildSettings(patterns);
        return Build(settings);
    }

    private static GlobExclusionFilter Build(FakeSettingsService settings)
    {
        MdIgnoreHierarchy hierarchy = new(new MdIgnoreReader(new FakeFileSystem()));
        return new GlobExclusionFilter(settings, hierarchy, NullLogger<GlobExclusionFilter>.Instance);
    }

    private static GlobExclusionFilter BuildWithMdIgnore(IReadOnlyList<string> patterns, string folder, string mdIgnoreContent)
    {
        FakeFileSystem fs = new();
        fs.AddDirectory(Root);
        fs.AddDirectory(folder);
        fs.AddFile(Path.Combine(folder, ".mdignore"), mdIgnoreContent + "\n", DateTime.UnixEpoch);
        MdIgnoreHierarchy hierarchy = new(new MdIgnoreReader(fs));
        return new GlobExclusionFilter(BuildSettings(patterns), hierarchy, NullLogger<GlobExclusionFilter>.Instance);
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
}
