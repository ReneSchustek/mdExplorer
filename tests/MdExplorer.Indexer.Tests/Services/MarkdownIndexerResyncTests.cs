using MdExplorer.Indexer.Abstractions;
using MdExplorer.Indexer.Models;
using Microsoft.Data.Sqlite;

namespace MdExplorer.Indexer.Tests.Services;

/// <summary>
/// Prüft die Selbstheilung des Indexers: den periodischen Abgleich, das Wiederholen beim Lesen
/// und den sofortigen Abgleich, wenn der Watcher Ereignisse verloren hat. Sie fangen Fälle auf,
/// die die Dateisystem-Überwachung nicht meldet: eine Datei, die während einer Netzwerktrennung
/// entsteht, eine, die im Moment des Lesens gesperrt ist, und alles nach einem Pufferüberlauf.
/// Ohne sie fehlt die Datei dauerhaft im Bestand, ohne dass irgendwo ein Fehler sichtbar würde.
/// </summary>
public sealed class MarkdownIndexerResyncTests
{
    // SQLITE_BUSY: so meldet SQLite eine Datenbank, die ein anderer Schreiber gerade hält.
    private const int SqliteBusy = 5;

    private const string Root = IndexerTestHarness.DefaultRoot;

    private static readonly DateTime FixedTime = new(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc);

    // Die Uhr ist eine Attrappe; der Wert bestimmt nur die Schrittweite beim Vorstellen.
    private static readonly TimeSpan ResyncInterval = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ResyncLoop_PicksUpAFileTheWatcherNeverReported()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create(resyncInterval: ResyncInterval);
        await StartWithResyncAsync(harness).ConfigureAwait(true);

        // Direkt am Dateisystem vorbei an der Überwachung, genau der Fall, den der
        // periodische Abgleich auffangen soll.
        string path = Path.Combine(Root, "still-entstanden.md");
        harness.FileSystem.AddFile(path, "# Still entstanden", FixedTime);
        harness.TimeProvider.Advance(ResyncInterval);

        await harness.WaitUntilAsync(() => harness.Store.Snapshot.ContainsKey(path), "Der periodische Abgleich hat die Datei nicht aufgenommen.").ConfigureAwait(true);
    }

    [Fact]
    public async Task ResyncLoop_RunsRepeatedly()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create(resyncInterval: ResyncInterval);
        await StartWithResyncAsync(harness).ConfigureAwait(true);

        await harness.RunOneResyncAsync(ResyncInterval).ConfigureAwait(true);
        await harness.RunOneResyncAsync(ResyncInterval).ConfigureAwait(true);
        await harness.RunOneResyncAsync(ResyncInterval).ConfigureAwait(true);

        // Je Takt ein Durchgang: Der Erst-Scan und drei Abgleiche haben die Wurzel gelesen.
        Assert.Equal(4, harness.Store.RootQueryCount);
    }

    [Fact]
    public async Task ResyncLoop_AfterADatabaseError_KeepsRunning()
    {
        // Eine vorübergehend belegte Datenbank ist kein Grund, den Abgleich für den Rest der
        // Sitzung einzustellen.
        await using IndexerTestHarness harness = IndexerTestHarness.Create(resyncInterval: ResyncInterval);
        await StartWithResyncAsync(harness).ConfigureAwait(true);
        harness.Store.ThrowOnNextRootQuery = new SqliteException("database is locked", SqliteBusy);
        await harness.RunOneResyncAsync(ResyncInterval).ConfigureAwait(true);

        string path = Path.Combine(Root, "nach-dem-fehler.md");
        harness.FileSystem.AddFile(path, "# Nach dem Fehler", FixedTime);
        harness.TimeProvider.Advance(ResyncInterval);

        await harness.WaitUntilAsync(() => harness.Store.Snapshot.ContainsKey(path), "Nach dem Datenbankfehler lief kein Abgleich mehr.").ConfigureAwait(true);
    }

    [Fact]
    public async Task ResyncLoop_WithoutChanges_ReportsNoProgress()
    {
        // Ein Abgleich, der nichts ändert, darf die Oberfläche nicht neu laden lassen. Bisher meldete
        // er sich wie ein Erst-Scan, und die Liste „Alle Dateien“ lud alle fünf Minuten neu.
        await using IndexerTestHarness harness = IndexerTestHarness.Create(resyncInterval: ResyncInterval);
        await StartWithResyncAsync(harness).ConfigureAwait(true);
        harness.FileSystem.AddFile(Path.Combine(Root, "a.md"), "# A", FixedTime);
        harness.TimeProvider.Advance(ResyncInterval);
        await harness.WaitUntilAsync(() => harness.Store.Snapshot.Count == 1, "Der Abgleich hat die Datei nicht aufgenommen.").ConfigureAwait(true);
        List<IndexerScanProgressEventArgs> reports = [];
        harness.Indexer.InitialScanProgress += (_, args) => reports.Add(args);

        await harness.RunOneResyncAsync(ResyncInterval).ConfigureAwait(true);
        await harness.RunOneResyncAsync(ResyncInterval).ConfigureAwait(true);

        Assert.Empty(reports);
    }

    [Fact]
    public async Task ResyncLoop_OnStop_EndsWithoutFault()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create(resyncInterval: ResyncInterval);
        await StartWithResyncAsync(harness).ConfigureAwait(true);

        await harness.Indexer.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Task? executeTask = harness.Indexer.ExecuteTask;
        Assert.NotNull(executeTask);
        Assert.True(executeTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ResyncLoop_WhenTheIntervalIsZero_DoesNotRun()
    {
        // Abgleich abgeschaltet: Eine an der Überwachung vorbei entstandene Datei bleibt
        // dann unbemerkt, sonst wäre die Einstellung wirkungslos.
        await using IndexerTestHarness harness = IndexerTestHarness.Create(resyncInterval: TimeSpan.Zero);
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);

        // Ein verarbeitetes Ereignis belegt, dass der Dienst über den Start des Abgleichs hinaus ist.
        harness.FileSystem.AddFile(Path.Combine(Root, "gemeldet.md"), "# Gemeldet", FixedTime);
        await harness.ProcessAsync(new FileSystemEvent(FileSystemEventKind.Created, Path.Combine(Root, "gemeldet.md"), OldPath: null, Root)).ConfigureAwait(true);

        Assert.Equal(0, harness.TimeProvider.PeriodicTimerCount);
        Assert.Equal(1, harness.Store.RootQueryCount);
    }

    [Fact]
    public async Task OnAWatcherBufferOverflow_SyncsTheRootAtOnce()
    {
        // Läuft der Puffer des Watchers über, sind Ereignisse verloren. Gewartet wird dann nicht auf
        // den nächsten Takt, sondern sofort abgeglichen; bei abgeschaltetem Abgleich käme sonst nie
        // einer.
        await using IndexerTestHarness harness = IndexerTestHarness.Create(resyncInterval: TimeSpan.Zero);
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);
        string lost = Path.Combine(Root, "verloren.md");
        harness.FileSystem.AddFile(lost, "# Verloren", FixedTime);

        harness.WatcherFor(Root).TriggerEvent(new FileSystemEvent(FileSystemEventKind.ResyncRequested, Root, OldPath: null, Root));

        await harness.WaitUntilAsync(() => harness.Store.Snapshot.ContainsKey(lost), "Nach dem Überlauf wurde nicht abgeglichen.").ConfigureAwait(true);
    }

    [Fact]
    public async Task InitialScan_WhenTheFileIsBrieflyLocked_RetriesAndIndexesIt()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        string path = Path.Combine(Root, "locked.md");
        harness.FileSystem.AddFile(path, "# Gesperrt", FixedTime);

        // Zwei Fehlschläge lässt der Indexer bei drei Versuchen noch zu.
        harness.FileSystem.FailOpenRead(path, times: 2);

        Task run = harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken);
        await AdvanceOnEachTimerUntilDoneAsync(harness, run).ConfigureAwait(true);

        Assert.True(harness.Store.Snapshot.ContainsKey(path), "Die kurzzeitig gesperrte Datei fehlt im Bestand.");
    }

    [Fact]
    public async Task InitialScan_WhenTheFileStaysLocked_SkipsItWithoutFailingTheScan()
    {
        // Nach dem Wiederholungsbudget wird die Datei übersprungen. Der Durchlauf muss
        // trotzdem zu Ende laufen, sonst blockiert eine einzige Datei den ganzen Bestand.
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        string locked = Path.Combine(Root, "dauerhaft-locked.md");
        string readable = Path.Combine(Root, "lesbar.md");
        harness.FileSystem.AddFile(locked, "# Gesperrt", FixedTime);
        harness.FileSystem.AddFile(readable, "# Lesbar", FixedTime);
        harness.FileSystem.FailOpenRead(locked, times: 99);

        Task run = harness.Indexer.RunInitialScanAsync(TestContext.Current.CancellationToken);
        await AdvanceOnEachTimerUntilDoneAsync(harness, run).ConfigureAwait(true);

        Assert.False(harness.Store.Snapshot.ContainsKey(locked));
        Assert.True(harness.Store.Snapshot.ContainsKey(readable), "Die lesbare Datei wurde mit übersprungen.");
    }

    [Fact]
    public async Task ResyncLoop_WithAnEventForTheSameFileMidway_WritesTheFileOnce()
    {
        // Abgleich und Ereignis schreiben nacheinander. Liefen sie nebeneinander, hielten beide die
        // neue Datei für unbekannt, und die zweite Anlage scheiterte am eindeutigen Pfad.
        await using IndexerTestHarness harness = IndexerTestHarness.Create(resyncInterval: ResyncInterval);
        await StartWithResyncAsync(harness).ConfigureAwait(true);
        string path = Path.Combine(Root, "neu.md");
        harness.FileSystem.AddFile(path, "# Neu", FixedTime);

        // Der Abgleich steht beim Speichern der neuen Datei, als ihr Ereignis eintrifft. Nur dieses
        // eine Speichern ist angehalten; ohne Sperre käme das Ereignis in der Lücke durch.
        harness.Store.HoldNextSave();
        long entered = harness.Store.SavesEntered.Mark;
        harness.TimeProvider.Advance(ResyncInterval);
        Assert.True(await harness.Store.SavesEntered.WaitAsync(entered, 1, IndexerTestHarness.Patience, TestContext.Current.CancellationToken).ConfigureAwait(true));
        int savesBefore = harness.Store.SaveCallCount;
        long writesBefore = harness.Store.Writes.Mark;
        harness.WatcherFor(Root).TriggerEvent(new FileSystemEvent(FileSystemEventKind.Created, path, OldPath: null, Root));
        harness.TimeProvider.Advance(TimeSpan.FromMilliseconds(IndexerTestHarness.DebounceMs));

        // Solange der Abgleich steht, speichert das Ereignis nicht. Die Frist kann nur zu einem
        // falschen Grün führen, nie zu einem falschen Rot: Ohne Sperre speichert es sofort.
        bool savedMeanwhile = await harness.Store.Writes.WaitAsync(writesBefore, 1, TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken).ConfigureAwait(true);
        harness.Store.ReleaseSaves();
        Assert.False(savedMeanwhile, "Das Ereignis hat gespeichert, während der Abgleich schrieb.");

        // Gespeichert werden der Stapel des Abgleichs und der des Ereignisses.
        await harness.WaitUntilAsync(() => harness.Store.SaveCallCount >= savesBefore + 2, "Abgleich und Ereignis haben nicht beide gespeichert.").ConfigureAwait(true);
        Assert.Equal(0, harness.Store.DuplicateRejectCount);
        Assert.True(harness.Store.Snapshot.ContainsKey(path));
    }

    private static async Task StartWithResyncAsync(IndexerTestHarness harness)
    {
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(false);
        Assert.True(
            await harness.TimeProvider.PeriodicTimersCreated.WaitAsync(0, 1, IndexerTestHarness.Patience, TestContext.Current.CancellationToken).ConfigureAwait(false),
            "Der Abgleich hat keinen Zeitgeber angelegt.");
    }

    /// <summary>
    /// Die Pause zwischen zwei Leseversuchen läuft über einen Zeitgeber. Jedes Mal, wenn einer
    /// angelegt wird, stellt der Test die Uhr um mehr als die längste Pause (100 ms) vor.
    /// </summary>
    private static async Task AdvanceOnEachTimerUntilDoneAsync(IndexerTestHarness harness, Task run)
    {
        while (!run.IsCompleted)
        {
            Task<bool> timerCreated = harness.TimeProvider.TimersCreated.WaitAsync(
                harness.TimeProvider.TimersCreated.Mark - harness.TimeProvider.Timers.Count(timer => !timer.IsDisposed),
                1,
                IndexerTestHarness.Patience,
                TestContext.Current.CancellationToken);
            if (await Task.WhenAny(run, timerCreated).ConfigureAwait(false) == timerCreated)
            {
                Assert.True(await timerCreated.ConfigureAwait(false), "Der Durchlauf hängt, ohne auf einen Zeitgeber zu warten.");
                harness.TimeProvider.Advance(TimeSpan.FromMilliseconds(200));
            }
        }
        await run.ConfigureAwait(false);
    }
}
