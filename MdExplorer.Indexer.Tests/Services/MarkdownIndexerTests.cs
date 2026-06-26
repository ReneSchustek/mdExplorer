using MdExplorer.Core.Models;
using MdExplorer.Indexer.Abstractions;
using MdExplorer.Indexer.Models;

namespace MdExplorer.Indexer.Tests.Services;

public sealed class MarkdownIndexerTests
{
    private static readonly DateTime FixedWrite = new(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RunInitialScanAsync_OnEmptyRepository_PersistsAllMarkdownFiles()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\a.md", "Inhalt A", FixedWrite);
        harness.FileSystem.AddFile(@"C:\Wurzel\sub\b.md", "Inhalt B", FixedWrite);

        await harness.Indexer.RunInitialScanAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, harness.Repository.Snapshot.Count);
        Assert.Contains(@"C:\Wurzel\a.md", harness.Repository.Snapshot.Keys);
        Assert.Contains(@"C:\Wurzel\sub\b.md", harness.Repository.Snapshot.Keys);
    }

    [Fact]
    public async Task RunInitialScanAsync_OnRepeatedRunWithoutChanges_DoesNotWrite()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\a.md", "Inhalt", FixedWrite);

        await harness.Indexer.RunInitialScanAsync(CancellationToken.None).ConfigureAwait(true);
        int writesAfterFirstScan = harness.Repository.TotalWrites;
        await harness.Indexer.RunInitialScanAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(writesAfterFirstScan, harness.Repository.TotalWrites);
    }

    [Fact]
    public async Task RunInitialScanAsync_OnDeletedFile_RemovesFromRepository()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\a.md", "Inhalt", FixedWrite);
        await harness.Indexer.RunInitialScanAsync(CancellationToken.None).ConfigureAwait(true);

        harness.FileSystem.RemoveFile(@"C:\Wurzel\a.md");
        await harness.Indexer.RunInitialScanAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(harness.Repository.Snapshot);
    }

    [Fact]
    public async Task RunInitialScanAsync_OnChangedContent_UpdatesHashAndMetadata()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\a.md", "Original", FixedWrite);
        await harness.Indexer.RunInitialScanAsync(CancellationToken.None).ConfigureAwait(true);
        string originalHash = harness.Repository.Snapshot[@"C:\Wurzel\a.md"].ContentHash;

        harness.FileSystem.UpdateFile(@"C:\Wurzel\a.md", "Neu", FixedWrite.AddMinutes(1));
        await harness.Indexer.RunInitialScanAsync(CancellationToken.None).ConfigureAwait(true);

        MarkdownFile updated = harness.Repository.Snapshot[@"C:\Wurzel\a.md"];
        Assert.NotEqual(originalHash, updated.ContentHash);
    }

    [Fact]
    public async Task ExecuteAsync_OnWatcherCreatedEvent_PersistsNewFile()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();

        await harness.Indexer.StartAsync(CancellationToken.None).ConfigureAwait(true);
        _ = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);

        harness.FileSystem.AddFile(@"C:\Wurzel\neu.md", "Neu", FixedWrite);
        harness.WatcherFor(IndexerTestHarness.DefaultRoot).TriggerEvent(
            new FileSystemEvent(FileSystemEventKind.Created, @"C:\Wurzel\neu.md", OldPath: null, IndexerTestHarness.DefaultRoot));
        harness.TimeProvider.Advance(TimeSpan.FromMilliseconds(300));

        bool persisted = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);
        Assert.True(persisted);
        Assert.Contains(@"C:\Wurzel\neu.md", harness.Repository.Snapshot.Keys);

        await harness.Indexer.StopAsync(CancellationToken.None).ConfigureAwait(true);
    }

    [Fact]
    public async Task ExecuteAsync_OnWatcherDeletedEvent_RemovesFile()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\a.md", "Inhalt", FixedWrite);

        await harness.Indexer.StartAsync(CancellationToken.None).ConfigureAwait(true);
        _ = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);
        _ = Assert.Single(harness.Repository.Snapshot);

        harness.FileSystem.RemoveFile(@"C:\Wurzel\a.md");
        harness.WatcherFor(IndexerTestHarness.DefaultRoot).TriggerEvent(
            new FileSystemEvent(FileSystemEventKind.Deleted, @"C:\Wurzel\a.md", OldPath: null, IndexerTestHarness.DefaultRoot));
        harness.TimeProvider.Advance(TimeSpan.FromMilliseconds(300));

        bool persisted = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);
        Assert.True(persisted);
        Assert.Empty(harness.Repository.Snapshot);

        await harness.Indexer.StopAsync(CancellationToken.None).ConfigureAwait(true);
    }

    [Fact]
    public async Task ExecuteAsync_OnWatcherRenamedEvent_UpdatesPath()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\alt.md", "Inhalt", FixedWrite);

        await harness.Indexer.StartAsync(CancellationToken.None).ConfigureAwait(true);
        _ = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);

        harness.FileSystem.RemoveFile(@"C:\Wurzel\alt.md");
        harness.FileSystem.AddFile(@"C:\Wurzel\neu.md", "Inhalt", FixedWrite);
        harness.WatcherFor(IndexerTestHarness.DefaultRoot).TriggerEvent(
            new FileSystemEvent(FileSystemEventKind.Renamed, @"C:\Wurzel\neu.md", @"C:\Wurzel\alt.md", IndexerTestHarness.DefaultRoot));
        harness.TimeProvider.Advance(TimeSpan.FromMilliseconds(300));

        bool persisted = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);
        Assert.True(persisted);
        Assert.Contains(@"C:\Wurzel\neu.md", harness.Repository.Snapshot.Keys);
        Assert.DoesNotContain(@"C:\Wurzel\alt.md", harness.Repository.Snapshot.Keys);

        await harness.Indexer.StopAsync(CancellationToken.None).ConfigureAwait(true);
    }

    // Wenn ein Batch beim SaveChanges crasht (DbException / IO / InvalidOperation),
    // darf die ConsumeEventsAsync-Schleife nicht abbrechen. Naechster Watcher-Event muss
    // weiterhin verarbeitet werden.
    [Fact]
    public async Task ProcessBatch_OnSaveChangesFailure_LogsAndContinuesWithNextEvent()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();

        await harness.Indexer.StartAsync(CancellationToken.None).ConfigureAwait(true);
        _ = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);

        // Erster Watcher-Event scheitert beim Save (kein Release, nur Throw).
        int saveCountBeforeBad = harness.Repository.SaveCallCount;
        harness.Repository.ThrowOnNextSave = new InvalidOperationException("simulated db spike");
        harness.FileSystem.AddFile(@"C:\Wurzel\bad.md", "Bad", FixedWrite);
        harness.WatcherFor(IndexerTestHarness.DefaultRoot).TriggerEvent(
            new FileSystemEvent(FileSystemEventKind.Created, @"C:\Wurzel\bad.md", OldPath: null, IndexerTestHarness.DefaultRoot));
        harness.TimeProvider.Advance(TimeSpan.FromMilliseconds(300));
        await WaitForAsync(
            () => harness.Repository.SaveCallCount > saveCountBeforeBad,
            Timeout).ConfigureAwait(true);

        // Service muss noch laufen — zweiter Event landet im Repo.
        harness.FileSystem.AddFile(@"C:\Wurzel\good.md", "Good", FixedWrite);
        harness.WatcherFor(IndexerTestHarness.DefaultRoot).TriggerEvent(
            new FileSystemEvent(FileSystemEventKind.Created, @"C:\Wurzel\good.md", OldPath: null, IndexerTestHarness.DefaultRoot));
        harness.TimeProvider.Advance(TimeSpan.FromMilliseconds(300));
        bool persisted = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);

        Assert.True(persisted);
        Assert.Contains(@"C:\Wurzel\good.md", harness.Repository.Snapshot.Values.Select(f => f.AbsolutePath));
        Task? executeTask = harness.Indexer.ExecuteTask;
        Assert.NotNull(executeTask);
        Assert.False(executeTask.IsCompleted, "Service-Loop darf nach Batch-Fehler nicht beendet sein.");
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(10).ConfigureAwait(false);
        }
        if (!condition())
        {
            throw new TimeoutException($"Bedingung nicht innerhalb von {timeout.TotalSeconds:F1}s erfuellt.");
        }
    }

    [Fact]
    public async Task ExecuteAsync_OnCancellation_StopsGracefullyWithoutException()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();

        await harness.Indexer.StartAsync(CancellationToken.None).ConfigureAwait(true);
        _ = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);

        await harness.Indexer.StopAsync(CancellationToken.None).ConfigureAwait(true);

        Task? executeTask = harness.Indexer.ExecuteTask;
        Assert.NotNull(executeTask);
        Assert.True(executeTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task RunInitialScanAsync_WhenNoConfiguredRoots_DoesNothing()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create(roots: Array.Empty<string>());

        await harness.Indexer.RunInitialScanAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(harness.Repository.Snapshot);
        Assert.Equal(0, harness.Repository.SaveCallCount);
    }

    [Fact]
    public async Task RunInitialScanAsync_OnLargeRoot_PersistsInBatchesAndFiresProgress()
    {
        // BatchSize = 2 → bei 5 Dateien erwarten wir 2 Zwischen-Saves + 1 Abschluss-Save.
        await using IndexerTestHarness harness = IndexerTestHarness.Create(initialScanBatchSize: 2);
        for (int i = 0; i < 5; i++)
        {
            harness.FileSystem.AddFile($@"C:\Wurzel\datei_{i}.md", $"Inhalt {i}", FixedWrite);
        }

        List<IndexerScanProgressEventArgs> events = [];
        harness.Indexer.InitialScanProgress += (_, args) => events.Add(args);

        await harness.Indexer.RunInitialScanAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(5, harness.Repository.Snapshot.Count);
        // Mindestens 3 Saves: zwei Batches plus Abschluss-Commit.
        Assert.True(harness.Repository.SaveCallCount >= 3,
            $"Erwartet >=3 SaveChanges-Aufrufe, gemessen {harness.Repository.SaveCallCount}.");
        Assert.NotEmpty(events);
        Assert.Contains(events, e => e.IsCompleted);
        IndexerScanProgressEventArgs completion = events.Last();
        Assert.True(completion.IsCompleted);
        Assert.Equal(5, completion.ProcessedCount);
        Assert.Equal(@"C:\Wurzel", completion.Root);
    }
}
