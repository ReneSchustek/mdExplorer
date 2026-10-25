using System.IO;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using static MdExplorer.App.Tests.ViewModels.FolderTreeViewModelTests;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Prüft die Randfälle des Ordnerbaums: Reaktion auf Einstellungsänderungen, Kommandos auf
/// ungeeigneten Knoten, nicht lesbare Verzeichnisse und den Ausweichpfad, wenn die Einstellungen
/// nicht geschrieben werden können. Diese Wege sind im Betrieb unauffällig, genau deshalb fällt
/// eine Regression hier erst dann auf, wenn der Baum beim Ändern der Wurzeln stehen bleibt oder
/// zusammenklappt.
/// </summary>
public sealed class FolderTreeViewModelEdgeCaseTests
{
    private const string Root = @"C:\wurzel";

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        FolderTreeViewModel sut = Create(FileSystemWith(Root), new FakeSettingsService(BuildSettings(Root)));

        sut.Dispose();
        sut.Dispose();
    }

    [Fact]
    public async Task SelectedNode_ClearedAgain_DropsThePathFilter()
    {
        // Beim Abwählen muss die Suche wieder global werden, sonst bleibt sie unsichtbar
        // auf den zuletzt angeklickten Ordner beschränkt.
        using FolderTreeViewModel sut = Create(FileSystemWith(Root, Root + @"\Unterordner"), new FakeSettingsService(BuildSettings(Root)));
        TreeNodeViewModel root = sut.Roots[0];
        await root.LoadChildrenAsync().ConfigureAwait(true);
        sut.SelectedNode = root.Children[0];
        Assert.NotNull(sut.SelectedPathPrefix);

        sut.SelectedNode = null;

        Assert.Null(sut.SelectedPathPrefix);
    }

    [Fact]
    public void SelectedNode_OutsideEveryRoot_LeavesTheSearchGlobal()
    {
        // Kann nach einem Wurzelwechsel auftreten, solange noch ein alter Knoten ausgewählt ist.
        const string foreign = @"C:\fremd";
        FakeFileSystem fs = FileSystemWith(Root, foreign);
        using FolderTreeViewModel sut = Create(fs, new FakeSettingsService(BuildSettings(Root)));

        sut.SelectedNode = new TreeNodeViewModel(
            foreign,
            "fremd",
            fs,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            exclusionFilter: null,
            rootAbsolutePath: foreign);

        Assert.Null(sut.SelectedPathPrefix);
    }

    [Fact]
    public async Task ResumeIndexing_WithoutNode_ChangesNothing()
    {
        FakeSettingsService settings = new(BuildSettings(Root));
        using FolderTreeViewModel sut = Create(FileSystemWith(Root), settings);

        await sut.ResumeIndexingCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(0, settings.SaveCount);
    }

    [Fact]
    public async Task ResumeIndexing_OnAFolderThatIsNotExcluded_ChangesNothing()
    {
        // "Wieder aufnehmen" auf einem laufenden Ordner darf keine Schreiboperation auslösen.
        FakeSettingsService settings = new(BuildSettings(Root));
        using FolderTreeViewModel sut = Create(FileSystemWith(Root, Root + @"\Aktiv"), settings);
        TreeNodeViewModel root = sut.Roots[0];
        await root.LoadChildrenAsync().ConfigureAwait(true);

        await sut.ResumeIndexingCommand.ExecuteAsync(root.Children[0]).ConfigureAwait(true);

        Assert.Equal(0, settings.SaveCount);
    }

    [Fact]
    public async Task PauseIndexing_WithoutNode_ChangesNothing()
    {
        FakeSettingsService settings = new(BuildSettings(Root));
        using FolderTreeViewModel sut = Create(FileSystemWith(Root), settings);

        await sut.PauseIndexingCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(0, settings.SaveCount);
    }

    [Fact]
    public async Task PauseIndexing_WhenTheSettingsCannotBeWritten_SaysSoAndKeepsTheTreeUsable()
    {
        // Eine schreibgeschützte Einstellungsdatei darf nicht dazu führen, dass ein
        // Rechtsklick die Anwendung beendet. Und der Nutzer muss erfahren, dass die Pause
        // nicht gespeichert ist; sonst wundert er sich, warum weiter indiziert wird.
        FakeSettingsService settings = new(BuildSettings(Root)) { SaveException = new IOException("Datei schreibgeschützt") };
        FakeDialogService dialog = new();
        using FolderTreeViewModel sut = Create(FileSystemWith(Root, Root + @"\Pausieren"), settings, dialog);
        TreeNodeViewModel root = sut.Roots[0];
        await root.LoadChildrenAsync().ConfigureAwait(true);

        await sut.PauseIndexingCommand.ExecuteAsync(root.Children[0]).ConfigureAwait(true);

        Assert.Equal(1, settings.SaveCount);
        _ = Assert.Single(sut.Roots);
        (string _, string message) = Assert.NotNull(dialog.LastError);
        Assert.Contains("Datei schreibgeschützt", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsChange_WithAnAdditionalRoot_RebuildsTheTree()
    {
        FakeSettingsService settings = new(BuildSettings(@"C:\a"));
        using FolderTreeViewModel sut = Create(FileSystemWith(@"C:\a", @"C:\b"), settings);
        _ = Assert.Single(sut.Roots);

        await settings.SaveAsync(BuildSettings(@"C:\a", @"C:\b"), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(2, sut.Roots.Count);
    }

    [Fact]
    public async Task SettingsChange_WithADifferentRoot_RebuildsTheTree()
    {
        // Gleiche Anzahl, anderer Pfad; ein reiner Anzahl-Vergleich würde das übersehen
        // und den Baum auf der alten Wurzel stehen lassen.
        FakeSettingsService settings = new(BuildSettings(@"C:\a"));
        using FolderTreeViewModel sut = Create(FileSystemWith(@"C:\a", @"C:\b"), settings);

        await settings.SaveAsync(BuildSettings(@"C:\b"), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(@"C:\b", Assert.Single(sut.Roots).AbsolutePath, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SettingsChange_WithUnchangedRoots_KeepsTheTreeExpanded()
    {
        // Nur die Ausschluss-Markierungen dürfen nachgezogen werden. Würde der Baum
        // neu gebaut, klappte er bei jeder Einstellungsänderung zusammen.
        const string subfolder = Root + @"\Unterordner";
        FakeFileSystem fs = FileSystemWith(Root, subfolder);
        fs.Files[Root + @"\notiz.md"] = [];
        FakeSettingsService settings = new(BuildSettings(Root));
        using FolderTreeViewModel sut = Create(fs, settings);
        TreeNodeViewModel root = sut.Roots[0];
        await root.LoadChildrenAsync().ConfigureAwait(true);
        int childrenBefore = root.Children.Count;

        await settings.SaveAsync(BuildSettings([Root], [subfolder]), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Same(root, sut.Roots[0]);
        Assert.Equal(childrenBefore, root.Children.Count);
        Assert.True(Assert.Single(root.Children, k => !k.IsMarkdownFile).IsExcluded);
    }

    [Fact]
    public async Task ExcludedFolders_WithBlankEntries_AreIgnored()
    {
        // Leere Einträge können aus einer handgepflegten Einstellungsdatei stammen;
        // sie dürfen keinen Ordner fälschlich als ausgeschlossen markieren.
        using FolderTreeViewModel sut = Create(FileSystemWith(Root, Root + @"\Aktiv"), new FakeSettingsService(BuildSettings([Root], ["   ", string.Empty])));
        TreeNodeViewModel root = sut.Roots[0];

        await root.LoadChildrenAsync().ConfigureAwait(true);

        Assert.False(root.IsExcluded);
        Assert.False(root.Children[0].IsExcluded);
    }

    [Fact]
    public async Task Expand_OnADirectoryThatIsGoneFromDisk_ListsNothing()
    {
        // Der Ordner steht noch in den Einstellungen, wurde aber inzwischen gelöscht. Das
        // Aufklappen darf dann nicht werfen.
        FakeFileSystem fs = FileSystemWith(Root);
        using FolderTreeViewModel sut = Create(fs, new FakeSettingsService(BuildSettings(Root)));
        TreeNodeViewModel root = Assert.Single(sut.Roots);
        _ = fs.Directories.Remove(Root);

        await root.LoadChildrenAsync().ConfigureAwait(true);

        Assert.Empty(root.Children);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Expand_WhenTheDirectoryCannotBeRead_ListsNothingInsteadOfThrowing(bool accessDenied)
    {
        // Ein getrenntes Netzlaufwerk meldet „Gerät nicht bereit“ als IOException, ein
        // gesperrter Ordner verweigert den Zugriff. Beides kam früher bis in den Setter.
        FakeFileSystem fs = FileSystemWith(Root);
        using FolderTreeViewModel sut = Create(fs, new FakeSettingsService(BuildSettings(Root)));
        fs.FailOnEnumerate = accessDenied ? new UnauthorizedAccessException("verweigert") : new IOException("Das Gerät ist nicht bereit.");

        await sut.Roots[0].LoadChildrenAsync().ConfigureAwait(true);

        Assert.Empty(sut.Roots[0].Children);
    }

    [Fact]
    public async Task Expand_WithAnExclusionFilter_HidesTheMatchingFiles()
    {
        // Der Ausschluss-Filter hält Entwurfsdateien aus dem Baum heraus, damit die
        // Anzeige zum Index passt, sonst klickt der Nutzer auf Dateien ohne Treffer.
        FakeFileSystem fs = FileSystemWith(Root);
        fs.Files[Root + @"\sichtbar.md"] = [];
        fs.Files[Root + @"\entwurf.md"] = [];
        using FolderTreeViewModel sut = new(
            new FakeSettingsService(BuildSettings(Root)),
            fs,
            new FakeDialogService(),
            NullLogger<FolderTreeViewModel>.Instance,
            new DraftFilter());
        TreeNodeViewModel root = sut.Roots[0];

        await root.LoadChildrenAsync().ConfigureAwait(true);

        Assert.Equal("sichtbar.md", Assert.Single(root.Children).DisplayName);
    }

    private static FolderTreeViewModel Create(FakeFileSystem fs, FakeSettingsService settings, FakeDialogService? dialog = null) =>
        new(settings, fs, dialog ?? new FakeDialogService(), NullLogger<FolderTreeViewModel>.Instance);

    /// <summary>Ausschluss-Filter, der alles mit „entwurf" im Namen ausblendet.</summary>
    private sealed class DraftFilter : MdExplorer.Indexer.Abstractions.IExclusionFilter
    {
        public bool IsExcluded(string absoluteFilePath, string rootAbsolutePath) =>
            absoluteFilePath.Contains("entwurf", StringComparison.OrdinalIgnoreCase);

        public void Invalidate()
        {
        }
    }
}
