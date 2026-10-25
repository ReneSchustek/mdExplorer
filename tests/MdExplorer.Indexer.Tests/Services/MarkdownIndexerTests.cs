using MdExplorer.Core.Models;
using MdExplorer.Data;
using MdExplorer.Indexer.Abstractions;
using MdExplorer.Indexer.Models;
using MdExplorer.Indexer.Tests.Fakes;

namespace MdExplorer.Indexer.Tests.Services;

/// <summary>
/// Der Indexer gleicht den Bestand beim Erst-Scan und über Watcher-Ereignisse mit dem Dateisystem ab:
/// neue Dateien kommen hinzu, geänderte bekommen einen neuen Hash, verschwundene und umbenannte
/// werden nachgezogen. Ein gescheitertes Speichern darf die Ereignisschleife nicht beenden, sonst
/// nimmt der Index bis zum nächsten Start keine Änderung mehr auf.
/// </summary>
public sealed class MarkdownIndexerTests
{
    private const string Root = IndexerTestHarness.DefaultRoot;

    private static readonly DateTime FixedWrite = new(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RunInitialScanAsync_OnEmptyRepository_PersistsAllMarkdownFiles()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\a.md", "Inhalt A", FixedWrite);
        harness.FileSystem.AddFile(@"C:\Wurzel\sub\b.md", "Inhalt B", FixedWrite);

        await harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(2, harness.Store.Snapshot.Count);
        Assert.Contains(@"C:\Wurzel\a.md", harness.Store.Snapshot.Keys);
        Assert.Contains(@"C:\Wurzel\sub\b.md", harness.Store.Snapshot.Keys);
    }

    [Fact]
    public async Task RunInitialScanAsync_OnRepeatedRunWithoutChanges_DoesNotWrite()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\a.md", "Inhalt", FixedWrite);

        await harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        int writesAfterFirstScan = harness.Store.TotalWrites;
        await harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(writesAfterFirstScan, harness.Store.TotalWrites);
    }

    [Fact]
    public async Task RunInitialScanAsync_OnRepeatedRunWithoutChanges_QueriesTheRootOnce()
    {
        // Ein Abgleich ohne Änderung fragt die Wurzel einmal als Ganzes ab und vergleicht im
        // Speicher. Eine Abfrage je Datei kostete bei 7.700 Notizen 7.700 Abfragen je Takt.
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        for (int i = 0; i < 5; i++)
        {
            harness.FileSystem.AddFile($@"C:\Wurzel\datei-{i}.md", "Inhalt", FixedWrite);
        }
        await harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        int queriesBefore = harness.Store.RootQueryCount;
        int lookupsBefore = harness.Store.PathLookupCount;

        await harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(1, harness.Store.RootQueryCount - queriesBefore);
        Assert.Equal(0, harness.Store.PathLookupCount - lookupsBefore);
    }

    [Fact]
    public async Task RunInitialScanAsync_OnDeletedFile_RemovesFromRepository()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\a.md", "Inhalt", FixedWrite);
        await harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        harness.FileSystem.RemoveFile(@"C:\Wurzel\a.md");
        await harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Empty(harness.Store.Snapshot);
    }

    [Fact]
    public async Task RunInitialScanAsync_OnChangedContent_UpdatesHashAndMetadata()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\a.md", "Original", FixedWrite);
        await harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        string originalHash = harness.Store.Snapshot[@"C:\Wurzel\a.md"].ContentHash;

        harness.FileSystem.UpdateFile(@"C:\Wurzel\a.md", "Neu", FixedWrite.AddMinutes(1));
        await harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        MarkdownFile updated = harness.Store.Snapshot[@"C:\Wurzel\a.md"];
        Assert.NotEqual(originalHash, updated.ContentHash);
    }

    [Fact]
    public async Task RunInitialScanAsync_WhenAFileVanishesDuringTheScan_IndexesTheRest()
    {
        // Eine Zwischendatei, die der Editor löscht, während der Scan sie gerade gefunden hat.
        // Brach daran der ganze Durchgang ab, fehlten alle übrigen Dateien und das Aufräumen fiel aus.
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\a-zwischen.md", "weg", FixedWrite);
        harness.FileSystem.AddFile(@"C:\Wurzel\b-bleibt.md", "Inhalt", FixedWrite);
        harness.FileSystem.AddFile(@"C:\Wurzel\c-bleibt.md", "Inhalt", FixedWrite);
        harness.FileSystem.VanishOnOpenRead(@"C:\Wurzel\a-zwischen.md");

        await harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(["b-bleibt.md", "c-bleibt.md"], harness.Store.Snapshot.Values.Select(file => file.RelativePath).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task RunInitialScanAsync_WhenTheDatabaseIsBusyForOneRoot_ScansTheOtherRoots()
    {
        // Eine belegte Datenbank beim Lesen einer Wurzel kostet diese Wurzel, nicht den Dienst.
        await using IndexerTestHarness harness = IndexerTestHarness.Create(roots: [@"C:\Eins", @"C:\Zwei"]);
        harness.FileSystem.AddFile(@"C:\Eins\a.md", "Inhalt", FixedWrite);
        harness.FileSystem.AddFile(@"C:\Zwei\b.md", "Inhalt", FixedWrite);
        harness.Store.ThrowOnNextRootQuery = new DatabaseOperationException("database is locked");

        await harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Contains(@"C:\Zwei\b.md", harness.Store.Snapshot.Keys);
    }

    [Fact]
    public async Task RunInitialScanAsync_CalledTwiceAtOnce_StoresEachFileOnce()
    {
        // Eine Einstellungsänderung startet einen Scan, während der Dienst selbst noch scannt.
        // Ohne gegenseitigen Ausschluss legten beide dieselben neuen Dateien an.
        await using IndexerTestHarness harness = IndexerTestHarness.Create(initialScanBatchSize: 1);
        for (int i = 0; i < 20; i++)
        {
            harness.FileSystem.AddFile($@"C:\Wurzel\datei-{i:D2}.md", $"Inhalt {i}", FixedWrite);
        }

        // Der erste Scan steht beim Speichern seiner ersten Portion, wenn der zweite beginnt.
        harness.Store.HoldSaves();
        long entered = harness.Store.SavesEntered.Mark;
        Task first = harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken);
        Assert.True(await harness.Store.SavesEntered.WaitAsync(entered, 1, IndexerTestHarness.Patience, TestContext.Current.CancellationToken).ConfigureAwait(true));
        Task second = harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken);
        harness.Store.ReleaseSaves();
        await Task.WhenAll(first, second).ConfigureAwait(true);

        // Jede Datei genau einmal geschrieben: Der zweite Scan fand den Bestand des ersten vor.
        Assert.Equal(20, harness.Store.Snapshot.Count);
        Assert.Equal(20, harness.Store.TotalWrites);
        Assert.Equal(0, harness.Store.DuplicateRejectCount);
    }

    [Fact]
    public async Task RunInitialScanAsync_OnLargeRoot_PersistsInBatchesAndFiresProgress()
    {
        // Portionsgröße 2 bei 5 Dateien ergibt zwei volle Portionen und einen Rest im Abschluss.
        await using IndexerTestHarness harness = IndexerTestHarness.Create(initialScanBatchSize: 2);
        for (int i = 0; i < 5; i++)
        {
            harness.FileSystem.AddFile($@"C:\Wurzel\datei_{i}.md", $"Inhalt {i}", FixedWrite);
        }

        List<IndexerScanProgressEventArgs> events = [];
        harness.Indexer.InitialScanProgress += (_, args) => events.Add(args);

        await harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(5, harness.Store.Snapshot.Count);
        Assert.Equal(3, harness.Store.SaveCallCount);
        IndexerScanProgressEventArgs completion = events[^1];
        Assert.True(completion.IsCompleted);
        Assert.Equal(5, completion.ProcessedCount);
        Assert.Equal(Root, completion.Root);
    }

    [Fact]
    public async Task RunInitialScanAsync_WhenNoConfiguredRoots_DoesNothing()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create(roots: []);

        await harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Empty(harness.Store.Snapshot);
        Assert.Equal(0, harness.Store.SaveCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_OnWatcherCreatedEvent_PersistsNewFile()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);

        harness.FileSystem.AddFile(@"C:\Wurzel\neu.md", "Neu", FixedWrite);
        await harness.ProcessAsync(Created(@"C:\Wurzel\neu.md")).ConfigureAwait(true);

        Assert.Contains(@"C:\Wurzel\neu.md", harness.Store.Snapshot.Keys);
    }

    [Fact]
    public async Task ExecuteAsync_OnWatcherDeletedEvent_RemovesFile()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\a.md", "Inhalt", FixedWrite);
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);

        harness.FileSystem.RemoveFile(@"C:\Wurzel\a.md");
        await harness.ProcessAsync(new FileSystemEvent(FileSystemEventKind.Deleted, @"C:\Wurzel\a.md", OldPath: null, Root)).ConfigureAwait(true);

        Assert.Empty(harness.Store.Snapshot);
    }

    [Fact]
    public async Task ExecuteAsync_OnWatcherRenamedEvent_UpdatesPathAndKeepsTheId()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\alt.md", "Inhalt", FixedWrite);
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);
        Guid originalId = harness.Store.Snapshot[@"C:\Wurzel\alt.md"].Id;

        harness.FileSystem.RemoveFile(@"C:\Wurzel\alt.md");
        harness.FileSystem.AddFile(@"C:\Wurzel\neu.md", "Inhalt", FixedWrite);
        await harness.ProcessAsync(Renamed(@"C:\Wurzel\neu.md", @"C:\Wurzel\alt.md")).ConfigureAwait(true);

        Assert.Equal(originalId, harness.Store.Snapshot[@"C:\Wurzel\neu.md"].Id);
        Assert.DoesNotContain(@"C:\Wurzel\alt.md", harness.Store.Snapshot.Keys);
    }

    [Fact]
    public async Task ExecuteAsync_OnARenameThatAlsoChangedTheContent_UpdatesTheHash()
    {
        // Der Rename-Zweig übernimmt Größe und Änderungszeit. Läse er den Inhalt dabei nicht
        // neu, hielte jeder spätere Abgleich die Datei für unverändert, und der alte Inhalt
        // bliebe dauerhaft im Index.
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\alt.md", "Inhalt", FixedWrite);
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);
        MarkdownFile original = harness.Store.Snapshot[@"C:\Wurzel\alt.md"];

        harness.FileSystem.RemoveFile(@"C:\Wurzel\alt.md");
        harness.FileSystem.AddFile(@"C:\Wurzel\neu.md", "Anderer Inhalt", FixedWrite.AddMinutes(1));
        await harness.ProcessAsync(Renamed(@"C:\Wurzel\neu.md", @"C:\Wurzel\alt.md")).ConfigureAwait(true);

        MarkdownFile renamed = harness.Store.Snapshot[@"C:\Wurzel\neu.md"];
        Assert.Equal(original.Id, renamed.Id);
        Assert.NotEqual(original.ContentHash, renamed.ContentHash);
    }

    [Fact]
    public async Task ExecuteAsync_OnSaveWithABackupFile_KeepsTheId()
    {
        // So speichern Editoren mit Sicherungsdatei: a.md → a.md~, dann die neue Fassung → a.md.
        // Ging der Eintrag dabei mit auf a.md~ über, entstand für a.md ein neuer mit neuer Id, und
        // Dokument und Schlagwort-Zuordnungen fielen bei jedem Speichern weg.
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\a.md", "Fassung 1", FixedWrite);
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);
        Guid originalId = harness.Store.Snapshot[@"C:\Wurzel\a.md"].Id;

        harness.FileSystem.AddFile(@"C:\Wurzel\a.md~", "Fassung 1", FixedWrite);
        harness.FileSystem.UpdateFile(@"C:\Wurzel\a.md", "Fassung 2", FixedWrite.AddMinutes(1));
        await harness.ProcessAsync(Renamed(@"C:\Wurzel\a.md~", @"C:\Wurzel\a.md")).ConfigureAwait(true);
        await harness.ProcessAsync(Renamed(@"C:\Wurzel\a.md", @"C:\Wurzel\a.tmp")).ConfigureAwait(true);

        MarkdownFile entry = Assert.Single(harness.Store.Snapshot.Values);
        Assert.Equal(@"C:\Wurzel\a.md", entry.AbsolutePath);
        Assert.Equal(originalId, entry.Id);
    }

    [Fact]
    public async Task ExecuteAsync_OnAnEventInAnExcludedFolder_DoesNotIndexTheFile()
    {
        // Pausierte Ordner und Ausschlussmuster gelten auch für Änderungen, die der Watcher meldet.
        // Sonst stünde jede Datei aus node_modules bis zum nächsten Abgleich im Index.
        await using IndexerTestHarness harness = IndexerTestHarness.Create(
            exclusionFilter: new Fakes.StubExclusionFilter(["node_modules"]));
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);

        harness.FileSystem.AddFile(@"C:\Wurzel\node_modules\dep.md", "Paket", FixedWrite);
        await harness.ProcessAsync(Created(@"C:\Wurzel\node_modules\dep.md")).ConfigureAwait(true);

        Assert.Empty(harness.Store.Snapshot);
    }

    [Fact]
    public async Task ExecuteAsync_OnTwoReleasedEventsForTheSameNewFileInOneBatch_StoresItOnce()
    {
        // Zwei freigegebene Ereignisse zur selben neuen Datei im selben Stapel, weil der Verbraucher
        // beschäftigt war. Fragte jedes nur die Datenbank, sähen beide „neu“ und legten zweimal an.
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);
        FakeFileWatcher watcher = harness.WatcherFor(Root);
        TimeSpan debounce = TimeSpan.FromMilliseconds(IndexerTestHarness.DebounceMs);

        // Der Verbraucher bleibt beim Speichern eines ersten Stapels stehen.
        harness.Store.HoldSaves();
        long enteredMark = harness.Store.SavesEntered.Mark;
        harness.FileSystem.AddFile(@"C:\Wurzel\vorher.md", "Vorher", FixedWrite);
        watcher.TriggerEvent(Created(@"C:\Wurzel\vorher.md"));
        harness.TimeProvider.Advance(debounce);
        Assert.True(await harness.Store.SavesEntered.WaitAsync(enteredMark, 1, IndexerTestHarness.Patience, TestContext.Current.CancellationToken).ConfigureAwait(true));

        // Währenddessen kommen beide Ereignisse zur neuen Datei im Kanal an.
        harness.FileSystem.AddFile(@"C:\Wurzel\neu.md", "Neu", FixedWrite);
        watcher.TriggerEvent(Created(@"C:\Wurzel\neu.md"));
        harness.TimeProvider.Advance(debounce);
        watcher.TriggerEvent(new FileSystemEvent(FileSystemEventKind.Changed, @"C:\Wurzel\neu.md", OldPath: null, Root));
        harness.TimeProvider.Advance(debounce);
        long writeMark = harness.Store.Writes.Mark;
        harness.Store.ReleaseSaves();

        // Zwei Stapel: der angehaltene und der mit beiden Ereignissen zur neuen Datei.
        Assert.True(await harness.Store.Writes.WaitAsync(writeMark, 2, IndexerTestHarness.Patience, TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Contains(@"C:\Wurzel\neu.md", harness.Store.Snapshot.Keys);
        Assert.Equal(2, harness.Store.Snapshot.Count);
    }

    // Scheitert das Speichern einer Portion, darf die Ereignisschleife nicht abbrechen; das nächste
    // Watcher-Ereignis wird weiter verarbeitet.
    [Fact]
    public async Task ProcessBatch_OnSaveChangesFailure_ContinuesWithTheNextEvent()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);

        harness.Store.ThrowOnNextSave = new DatabaseOperationException("Speichern fehlgeschlagen.");
        harness.FileSystem.AddFile(@"C:\Wurzel\bad.md", "Bad", FixedWrite);
        await harness.ProcessAsync(Created(@"C:\Wurzel\bad.md")).ConfigureAwait(true);

        harness.FileSystem.AddFile(@"C:\Wurzel\good.md", "Good", FixedWrite);
        await harness.ProcessAsync(Created(@"C:\Wurzel\good.md")).ConfigureAwait(true);

        Assert.Contains(@"C:\Wurzel\good.md", harness.Store.Snapshot.Keys);
        Task? executeTask = harness.Indexer.ExecuteTask;
        Assert.NotNull(executeTask);
        Assert.False(executeTask.IsCompleted, "Service-Loop darf nach Batch-Fehler nicht beendet sein.");
    }

    [Fact]
    public async Task ExecuteAsync_OnCancellation_StopsGracefullyWithoutException()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);

        await harness.Indexer.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Task? executeTask = harness.Indexer.ExecuteTask;
        Assert.NotNull(executeTask);
        Assert.True(executeTask.IsCompletedSuccessfully);
    }

    private static FileSystemEvent Created(string path) =>
        new(FileSystemEventKind.Created, path, OldPath: null, Root);

    private static FileSystemEvent Renamed(string path, string oldPath) =>
        new(FileSystemEventKind.Renamed, path, oldPath, Root);
}
