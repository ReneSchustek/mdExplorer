using MdExplorer.Core.Models;
using MdExplorer.Indexer.Models;
using MdExplorer.Indexer.Tests.Fakes;

namespace MdExplorer.Indexer.Tests.Services;

/// <summary>
/// Deckt die Randfälle des Indexers ab: fehlende oder unbrauchbare Wurzeln, Umbenennungen
/// ohne verwertbaren Vorgängerpfad und Änderungsereignisse, die gar keine Änderung sind.
/// Diese Pfade treten im Betrieb selten auf und brechen deshalb unbemerkt.
/// </summary>
/// <remarks>
/// Eine negative Prüfung („schreibt nichts“) kommt erst nach dem Speichern des Stapels. Der
/// Verbraucher speichert jeden Stapel, auch einen leeren; danach ist ein Schreibzugriff wirklich
/// ausgeblieben und nicht bloß noch nicht passiert.
/// </remarks>
public sealed class MarkdownIndexerEdgeCaseTests
{
    private const string Root = IndexerTestHarness.DefaultRoot;
    private static readonly DateTime FixedWrite = new(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ExecuteAsync_WithoutAnyConfiguredRoot_StopsWithoutWatchers()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create(roots: []);

        await harness.Indexer.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        await harness.Indexer.ExecuteTask!.WaitAsync(IndexerTestHarness.Patience, TestContext.Current.CancellationToken).ConfigureAwait(true); // gesetzt durch StartAsync

        // Ohne Wurzel gibt es nichts zu beobachten; der Dienst darf dann auch keinen
        // Beobachter anlegen, statt leer weiterzulaufen.
        Assert.Empty(harness.WatcherFactory.Watchers);
    }

    [Fact]
    public async Task ExecuteAsync_WithBlankRoot_IgnoresIt()
    {
        // Der leere Eintrag wird nachträglich gesetzt: Der Prüfstand legt für jede Wurzel ein
        // Verzeichnis an und käme mit einer leeren Angabe gar nicht erst zustande.
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        await SetRootsAsync(harness, ["   ", Root]).ConfigureAwait(true);

        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);

        _ = Assert.Single(harness.WatcherFactory.Watchers);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingRootDirectory_IgnoresIt()
    {
        // Die Wurzel steht in den Einstellungen, das Verzeichnis gibt es aber nicht, etwa
        // ein abgezogenes Netzlaufwerk. Der Indexer darf daran nicht scheitern.
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        await SetRootsAsync(harness, [Root, @"C:\GibtEsNicht"]).ConfigureAwait(true);

        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);

        Assert.Equal([Root], harness.WatcherFactory.Watchers.Keys);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheWatcherOfOneRootCannotStart_StillIndexesAllRoots()
    {
        // Der Watcher eines Netzlaufwerks wirft beim Start. Das kostet die Live-Aktualisierung
        // dieser Wurzel, nicht den Dienst: Erst-Scan und Abgleich laufen weiter.
        FakeFileWatcherFactory factory = new();
        _ = factory.FailingRoots.Add(@"C:\Netz");
        await using IndexerTestHarness harness = IndexerTestHarness.Create(roots: [Root, @"C:\Netz"], watcherFactory: factory);
        harness.FileSystem.AddFile(@"C:\Netz\a.md", "Inhalt", FixedWrite);

        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);

        Assert.Contains(@"C:\Netz\a.md", harness.Store.Snapshot.Keys);
        Assert.False(harness.Indexer.ExecuteTask!.IsCompleted, "Der Dienst muss weiterlaufen.");
        Assert.True(harness.WatcherFor(@"C:\Netz").IsDisposed, "Der gescheiterte Watcher muss freigegeben sein.");
    }

    [Fact]
    public async Task OnRenameWithoutOldPath_TreatsItAsANewFile()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);

        harness.FileSystem.AddFile(@"C:\Wurzel\ohne-vorlage.md", "Inhalt", FixedWrite);
        await harness.ProcessAsync(Renamed(@"C:\Wurzel\ohne-vorlage.md", oldPath: null)).ConfigureAwait(true);

        Assert.Contains(@"C:\Wurzel\ohne-vorlage.md", harness.Store.Snapshot.Keys);
    }

    [Fact]
    public async Task OnRenameWithUnknownOldPath_TreatsItAsANewFile()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);

        harness.FileSystem.AddFile(@"C:\Wurzel\ziel.md", "Inhalt", FixedWrite);
        await harness.ProcessAsync(Renamed(@"C:\Wurzel\ziel.md", @"C:\Wurzel\nie-indiziert.md")).ConfigureAwait(true);

        Assert.Contains(@"C:\Wurzel\ziel.md", harness.Store.Snapshot.Keys);
    }

    [Fact]
    public async Task OnRenameOutOfTheRoot_RemovesTheOldEntry()
    {
        // Umbenennung aus dem beobachteten Bereich heraus: Das Ziel liegt nicht mehr da,
        // wo der Indexer schaut. Der alte Eintrag muss verschwinden statt zu verwaisen.
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\alt.md", "Inhalt", FixedWrite);
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);

        harness.FileSystem.RemoveFile(@"C:\Wurzel\alt.md");
        harness.FileSystem.AddFile(@"C:\Woanders\alt.md", "Inhalt", FixedWrite);
        await harness.ProcessAsync(Renamed(@"C:\Woanders\alt.md", @"C:\Wurzel\alt.md")).ConfigureAwait(true);

        Assert.Empty(harness.Store.Snapshot);
    }

    [Fact]
    public async Task OnRenameIntoAnExistingEntry_ReplacesItWithoutADuplicate()
    {
        // Speichern über eine Umbenennung auf eine schon indizierte Datei: Der Eintrag der
        // ersetzten Datei muss weichen, sonst stünde derselbe Pfad zweimal im Index.
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\entwurf.md", "Neu", FixedWrite);
        harness.FileSystem.AddFile(@"C:\Wurzel\ziel.md", "Alt", FixedWrite);
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);
        Guid draftId = harness.Store.Snapshot[@"C:\Wurzel\entwurf.md"].Id;

        harness.FileSystem.RemoveFile(@"C:\Wurzel\entwurf.md");
        harness.FileSystem.UpdateFile(@"C:\Wurzel\ziel.md", "Neu", FixedWrite.AddMinutes(1));
        await harness.ProcessAsync(Renamed(@"C:\Wurzel\ziel.md", @"C:\Wurzel\entwurf.md")).ConfigureAwait(true);

        MarkdownFile entry = Assert.Single(harness.Store.Snapshot.Values);
        Assert.Equal(@"C:\Wurzel\ziel.md", entry.AbsolutePath);
        Assert.Equal(draftId, entry.Id);
    }

    [Fact]
    public async Task OnChangeEventForMissingFile_DoesNotWrite()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);
        int before = harness.Store.TotalWrites;

        await harness.ProcessAsync(Changed(@"C:\Wurzel\gibt-es-nicht.md")).ConfigureAwait(true);

        Assert.Equal(before, harness.Store.TotalWrites);
    }

    [Fact]
    public async Task OnChangeEventWithoutRealChange_DoesNotRewriteTheEntry()
    {
        // Gleiche Größe, gleicher Schreibzeitpunkt: Der Beobachter feuert trotzdem, etwa
        // beim Öffnen in einem Editor. Ein Schreibvorgang wäre hier reine Last.
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\gleichbleibend.md", "Immer gleich", FixedWrite);
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);
        int before = harness.Store.TotalWrites;

        await harness.ProcessAsync(Changed(@"C:\Wurzel\gleichbleibend.md")).ConfigureAwait(true);

        Assert.Equal(before, harness.Store.TotalWrites);
    }

    [Fact]
    public async Task OnTouchedFileWithSameContent_KeepsTheContentHash()
    {
        // Nur der Zeitstempel wandert (etwa durch ein Backup-Werkzeug), der Inhalt bleibt.
        // Der Eintrag muss aktualisiert werden, der Hash aber derselbe bleiben.
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\touched.md", "Gleicher Inhalt", FixedWrite);
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);
        string hashBefore = harness.Store.Snapshot[@"C:\Wurzel\touched.md"].ContentHash;

        harness.FileSystem.UpdateFile(@"C:\Wurzel\touched.md", "Gleicher Inhalt", FixedWrite.AddHours(1));
        await harness.ProcessAsync(Changed(@"C:\Wurzel\touched.md")).ConfigureAwait(true);

        MarkdownFile after = harness.Store.Snapshot[@"C:\Wurzel\touched.md"];
        Assert.Equal(hashBefore, after.ContentHash, StringComparer.Ordinal);
        Assert.Equal(FixedWrite.AddHours(1), after.LastWriteTimeUtc);
    }

    [Fact]
    public async Task OnDeleteEventForUnknownFile_DoesNotWrite()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);
        int before = harness.Store.TotalWrites;

        await harness.ProcessAsync(new FileSystemEvent(FileSystemEventKind.Deleted, @"C:\Wurzel\war-nie-da.md", OldPath: null, Root)).ConfigureAwait(true);

        Assert.Equal(before, harness.Store.TotalWrites);
    }

    private static async Task SetRootsAsync(IndexerTestHarness harness, string[] roots) =>
        await harness.Settings.SaveAsync(
            harness.Settings.Current with
            {
                Indexing = harness.Settings.Current.Indexing with { Roots = roots },
            },
            TestContext.Current.CancellationToken).ConfigureAwait(false);

    private static FileSystemEvent Changed(string path) =>
        new(FileSystemEventKind.Changed, path, OldPath: null, Root);

    private static FileSystemEvent Renamed(string path, string? oldPath) =>
        new(FileSystemEventKind.Renamed, path, oldPath, Root);
}
