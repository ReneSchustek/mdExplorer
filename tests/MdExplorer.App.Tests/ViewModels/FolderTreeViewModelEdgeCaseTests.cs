using System.IO;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Prüft die Randfälle des Ordnerbaums: Reaktion auf Einstellungsänderungen, Kommandos auf
/// ungeeigneten Knoten und den Ausweichpfad, wenn die Einstellungen nicht geschrieben werden
/// können. Diese Wege sind im Betrieb unauffällig — genau deshalb fällt eine Regression hier
/// erst dann auf, wenn der Baum beim Ändern der Wurzeln stehen bleibt oder zusammenklappt.
/// </summary>
public sealed class FolderTreeViewModelEdgeCaseTests : IDisposable
{
    private readonly string _basis = Path.Combine(Path.GetTempPath(), "mdexp-tree-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        string wurzel = ErzeugeOrdner("wurzel");
        FakeFileSystem fs = ErzeugeDateisystem(wurzel);
        StubSettings einstellungen = new(BaueEinstellungen(wurzel));
        FolderTreeViewModel sut = new(einstellungen, fs);

        sut.Dispose();
        sut.Dispose();
    }

    [Fact]
    public void SelectedNode_ClearedAgain_DropsThePathFilter()
    {
        // Beim Abwählen muss die Suche wieder global werden, sonst bleibt sie unsichtbar
        // auf den zuletzt angeklickten Ordner beschränkt.
        string wurzel = ErzeugeOrdner("wurzel");
        _ = ErzeugeOrdner("wurzel", "Unterordner");
        FakeFileSystem fs = ErzeugeDateisystem(wurzel);
        StubSettings einstellungen = new(BaueEinstellungen(wurzel));
        using FolderTreeViewModel sut = new(einstellungen, fs);
        TreeNodeViewModel root = sut.Roots[0];
        root.IsExpanded = true;
        sut.SelectedNode = root.Children[0];
        Assert.NotNull(sut.SelectedPathPrefix);

        sut.SelectedNode = null;

        Assert.Null(sut.SelectedPathPrefix);
    }

    [Fact]
    public void SelectedNode_OutsideEveryRoot_LeavesTheSearchGlobal()
    {
        // Kann nach einem Wurzelwechsel auftreten, solange noch ein alter Knoten ausgewählt ist.
        string wurzel = ErzeugeOrdner("wurzel");
        string fremd = ErzeugeOrdner("fremd");
        FakeFileSystem fs = ErzeugeDateisystem(wurzel, fremd);
        StubSettings einstellungen = new(BaueEinstellungen(wurzel));
        using FolderTreeViewModel sut = new(einstellungen, fs);

        sut.SelectedNode = new TreeNodeViewModel(
            fremd,
            "fremd",
            fs,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            exclusionFilter: null,
            rootAbsolutePath: fremd);

        Assert.Null(sut.SelectedPathPrefix);
    }

    [Fact]
    public async Task ResumeIndexing_WithoutNode_ChangesNothing()
    {
        string wurzel = ErzeugeOrdner("wurzel");
        FakeFileSystem fs = ErzeugeDateisystem(wurzel);
        StubSettings einstellungen = new(BaueEinstellungen(wurzel));
        using FolderTreeViewModel sut = new(einstellungen, fs);

        await sut.ResumeIndexingCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(0, einstellungen.SpeicherAufrufe);
    }

    [Fact]
    public async Task ResumeIndexing_OnAFolderThatIsNotExcluded_ChangesNothing()
    {
        // "Wieder aufnehmen" auf einem laufenden Ordner darf keine Schreiboperation auslösen.
        string wurzel = ErzeugeOrdner("wurzel");
        _ = ErzeugeOrdner("wurzel", "Aktiv");
        FakeFileSystem fs = ErzeugeDateisystem(wurzel);
        StubSettings einstellungen = new(BaueEinstellungen(wurzel));
        using FolderTreeViewModel sut = new(einstellungen, fs);
        TreeNodeViewModel root = sut.Roots[0];
        root.IsExpanded = true;

        await sut.ResumeIndexingCommand.ExecuteAsync(root.Children[0]).ConfigureAwait(true);

        Assert.Equal(0, einstellungen.SpeicherAufrufe);
    }

    [Fact]
    public async Task PauseIndexing_WithoutNode_ChangesNothing()
    {
        string wurzel = ErzeugeOrdner("wurzel");
        FakeFileSystem fs = ErzeugeDateisystem(wurzel);
        StubSettings einstellungen = new(BaueEinstellungen(wurzel));
        using FolderTreeViewModel sut = new(einstellungen, fs);

        await sut.PauseIndexingCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(0, einstellungen.SpeicherAufrufe);
    }

    [Fact]
    public async Task PauseIndexing_WhenTheSettingsCannotBeWritten_KeepsTheTreeUsable()
    {
        // Eine schreibgeschützte Einstellungsdatei darf nicht dazu führen, dass ein
        // Rechtsklick die Anwendung beendet.
        string wurzel = ErzeugeOrdner("wurzel");
        _ = ErzeugeOrdner("wurzel", "Pausieren");
        FakeFileSystem fs = ErzeugeDateisystem(wurzel);
        StubSettings einstellungen = new(BaueEinstellungen(wurzel))
        {
            SpeicherFehler = new IOException("Datei schreibgeschuetzt"),
        };
        using FolderTreeViewModel sut = new(einstellungen, fs);
        TreeNodeViewModel root = sut.Roots[0];
        root.IsExpanded = true;

        await sut.PauseIndexingCommand.ExecuteAsync(root.Children[0]).ConfigureAwait(true);

        Assert.Equal(1, einstellungen.SpeicherAufrufe);
        _ = Assert.Single(sut.Roots);
    }

    [Fact]
    public async Task SettingsChange_WithAnAdditionalRoot_RebuildsTheTree()
    {
        string wurzelA = ErzeugeOrdner("a");
        string wurzelB = ErzeugeOrdner("b");
        FakeFileSystem fs = ErzeugeDateisystem(wurzelA, wurzelB);
        StubSettings einstellungen = new(BaueEinstellungen(wurzelA));
        using FolderTreeViewModel sut = new(einstellungen, fs);
        _ = Assert.Single(sut.Roots);

        await einstellungen.SaveAsync(BaueEinstellungen(wurzelA, wurzelB), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(2, sut.Roots.Count);
    }

    [Fact]
    public async Task SettingsChange_WithADifferentRoot_RebuildsTheTree()
    {
        // Gleiche Anzahl, anderer Pfad — ein reiner Anzahl-Vergleich würde das übersehen
        // und den Baum auf der alten Wurzel stehen lassen.
        string wurzelA = ErzeugeOrdner("a");
        string wurzelB = ErzeugeOrdner("b");
        FakeFileSystem fs = ErzeugeDateisystem(wurzelA, wurzelB);
        StubSettings einstellungen = new(BaueEinstellungen(wurzelA));
        using FolderTreeViewModel sut = new(einstellungen, fs);

        await einstellungen.SaveAsync(BaueEinstellungen(wurzelB), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(wurzelB, Assert.Single(sut.Roots).AbsolutePath, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SettingsChange_WithUnchangedRoots_KeepsTheTreeExpanded()
    {
        // Nur die Ausschluss-Markierungen dürfen nachgezogen werden. Würde der Baum
        // neu gebaut, klappte er bei jeder Einstellungsänderung zusammen.
        string wurzel = ErzeugeOrdner("wurzel");
        string unterordner = ErzeugeOrdner("wurzel", "Unterordner");
        await File.WriteAllTextAsync(Path.Combine(wurzel, "notiz.md"), "# Notiz", TestContext.Current.CancellationToken).ConfigureAwait(true);
        FakeFileSystem fs = ErzeugeDateisystem(wurzel);
        StubSettings einstellungen = new(BaueEinstellungen(wurzel));
        using FolderTreeViewModel sut = new(einstellungen, fs);
        TreeNodeViewModel root = sut.Roots[0];
        root.IsExpanded = true;
        int kinderVorher = root.Children.Count;

        await einstellungen.SaveAsync(
            BaueEinstellungen([wurzel], [unterordner]),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Same(root, sut.Roots[0]);
        Assert.Equal(kinderVorher, root.Children.Count);
        Assert.True(Assert.Single(root.Children, k => !k.IsMarkdownFile).IsExcluded);
    }

    [Fact]
    public void ExcludedFolders_WithBlankEntries_AreIgnored()
    {
        // Leere Einträge können aus einer handgepflegten Einstellungsdatei stammen;
        // sie dürfen keinen Ordner fälschlich als ausgeschlossen markieren.
        string wurzel = ErzeugeOrdner("wurzel");
        _ = ErzeugeOrdner("wurzel", "Aktiv");
        FakeFileSystem fs = ErzeugeDateisystem(wurzel);
        StubSettings einstellungen = new(BaueEinstellungen([wurzel], ["   ", string.Empty]));
        using FolderTreeViewModel sut = new(einstellungen, fs);
        TreeNodeViewModel root = sut.Roots[0];

        root.IsExpanded = true;

        Assert.False(root.IsExcluded);
        Assert.False(root.Children[0].IsExcluded);
    }

    [Fact]
    public void Expand_OnADirectoryThatIsGoneFromDisk_ListsNoFiles()
    {
        // Der Ordner steht noch in den Einstellungen, wurde aber gelöscht oder liegt auf
        // einem getrennten Netzlaufwerk. Das Aufklappen darf dann nicht werfen.
        string wurzel = Path.Combine(_basis, "verschwunden");
        FakeFileSystem fs = ErzeugeDateisystem(wurzel);
        StubSettings einstellungen = new(BaueEinstellungen(wurzel));
        using FolderTreeViewModel sut = new(einstellungen, fs);
        TreeNodeViewModel root = Assert.Single(sut.Roots);

        root.IsExpanded = true;

        Assert.Empty(root.Children);
    }

    [Fact]
    public void Expand_OnANodeOutsideTheKnownDirectories_ListsNoFiles()
    {
        string wurzel = ErzeugeOrdner("wurzel");
        string unbekannt = ErzeugeOrdner("wurzel", "Unbekannt");
        FakeFileSystem fs = ErzeugeDateisystem(wurzel);
        StubSettings einstellungen = new(BaueEinstellungen(wurzel));
        using FolderTreeViewModel sut = new(einstellungen, fs);
        TreeNodeViewModel root = sut.Roots[0];
        root.IsExpanded = true;
        TreeNodeViewModel kind = Assert.Single(root.Children, k => string.Equals(k.AbsolutePath, unbekannt, StringComparison.OrdinalIgnoreCase));

        kind.IsExpanded = true;

        Assert.Empty(kind.Children);
    }

    [Fact]
    public async Task Expand_WithAnExclusionFilter_HidesTheMatchingFiles()
    {
        // Der Ausschluss-Filter hält Entwurfsdateien aus dem Baum heraus, damit die
        // Anzeige zum Index passt — sonst klickt der Nutzer auf Dateien ohne Treffer.
        string wurzel = ErzeugeOrdner("wurzel");
        await File.WriteAllTextAsync(Path.Combine(wurzel, "sichtbar.md"), "# Sichtbar", TestContext.Current.CancellationToken).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(wurzel, "entwurf.md"), "# Entwurf", TestContext.Current.CancellationToken).ConfigureAwait(true);
        FakeFileSystem fs = ErzeugeDateisystem(wurzel);
        StubSettings einstellungen = new(BaueEinstellungen(wurzel));
        using FolderTreeViewModel sut = new(einstellungen, fs, new EntwurfsFilter());
        TreeNodeViewModel root = sut.Roots[0];

        root.IsExpanded = true;

        Assert.Contains(root.Children, k => string.Equals(k.DisplayName, "sichtbar.md", StringComparison.Ordinal));
        Assert.DoesNotContain(root.Children, k => string.Equals(k.DisplayName, "entwurf.md", StringComparison.Ordinal));
    }

    /// <summary>Ausschluss-Filter, der alles mit „entwurf" im Namen ausblendet.</summary>
    private sealed class EntwurfsFilter : MdExplorer.Indexer.Abstractions.IExclusionFilter
    {
        public bool IsExcluded(string absoluteFilePath, string rootAbsolutePath) =>
            absoluteFilePath.Contains("entwurf", StringComparison.OrdinalIgnoreCase);

        public void Invalidate()
        {
        }
    }

    private string ErzeugeOrdner(params string[] teile)
    {
        string pfad = Path.Combine([_basis, .. teile]);
        _ = Directory.CreateDirectory(pfad);
        return pfad;
    }

    private static FakeFileSystem ErzeugeDateisystem(params string[] verzeichnisse)
    {
        FakeFileSystem fs = new();
        foreach (string verzeichnis in verzeichnisse)
        {
            _ = fs.Directories.Add(verzeichnis);
        }
        return fs;
    }

    private static AppSettings BaueEinstellungen(params string[] wurzeln) => BaueEinstellungen(wurzeln, []);

    private static AppSettings BaueEinstellungen(IReadOnlyList<string> wurzeln, IReadOnlyList<string> ausgeschlossen) => new(
        AppSettings.CurrentSchemaVersion,
        new IndexingSettings(wurzeln, IndexingSettings.DefaultExclusionPatterns, ausgeschlossen, true),
        AppearanceSettings.Default,
        BehaviorSettings.Default);

    public void Dispose()
    {
        if (Directory.Exists(_basis))
        {
            Directory.Delete(_basis, recursive: true);
        }
    }

    /// <summary>Einstellungsdienst, der Schreibvorgänge zählt und wahlweise scheitern lässt.</summary>
    private sealed class StubSettings : ISettingsService
    {
        public StubSettings(AppSettings initial) => Current = initial;

        public AppSettings Current { get; private set; }

        public Exception? SpeicherFehler { get; init; }

        public int SpeicherAufrufe { get; private set; }

        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(settings);
            SpeicherAufrufe++;
            if (SpeicherFehler is not null)
            {
                return Task.FromException(SpeicherFehler);
            }
            AppSettings vorher = Current;
            Current = settings;
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(vorher, settings));
            return Task.CompletedTask;
        }
    }
}
