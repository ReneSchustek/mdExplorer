using MdExplorer.Core.Models;
using MdExplorer.Indexer.Models;

namespace MdExplorer.Indexer.Tests.Services;

/// <summary>
/// Deckt die Randfälle des Indexers ab: fehlende oder unbrauchbare Wurzeln, Umbenennungen
/// ohne verwertbaren Vorgängerpfad und Änderungsereignisse, die gar keine Änderung sind.
/// Diese Pfade treten im Betrieb selten auf und brechen deshalb unbemerkt.
/// </summary>
public sealed class MarkdownIndexerEdgeCaseTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly DateTime FixedWrite = new(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ExecuteAsync_WithoutAnyConfiguredRoot_StopsWithoutWatchers()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create(roots: []);

        await harness.Indexer.StartAsync(CancellationToken.None).ConfigureAwait(true);

        // Ohne Wurzel gibt es nichts zu beobachten — der Dienst darf dann auch keinen
        // Beobachter anlegen, statt leer weiterzulaufen.
        Assert.Empty(harness.WatcherFactory.Watchers);

        await harness.Indexer.StopAsync(CancellationToken.None).ConfigureAwait(true);
    }

    [Fact]
    public async Task ExecuteAsync_WithBlankRoot_IgnoresIt()
    {
        // Der leere Eintrag wird nachträglich gesetzt: Der Prüfstand legt für jede Wurzel ein
        // Verzeichnis an und käme mit einer leeren Angabe gar nicht erst zustande.
        await using IndexerTestHarness harness = IndexerTestHarness.Create(roots: [IndexerTestHarness.DefaultRoot]);
        await harness.Settings.SaveAsync(
            harness.Settings.Current with
            {
                Indexing = harness.Settings.Current.Indexing with
                {
                    Roots = ["   ", IndexerTestHarness.DefaultRoot],
                },
            },
            CancellationToken.None).ConfigureAwait(true);

        await harness.Indexer.StartAsync(CancellationToken.None).ConfigureAwait(true);
        _ = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);

        _ = Assert.Single(harness.WatcherFactory.Watchers);

        await harness.Indexer.StopAsync(CancellationToken.None).ConfigureAwait(true);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingRootDirectory_IgnoresIt()
    {
        // Die Wurzel steht in den Einstellungen, das Verzeichnis gibt es aber nicht — etwa
        // ein abgezogenes Netzlaufwerk. Der Indexer darf daran nicht scheitern.
        await using IndexerTestHarness harness = IndexerTestHarness.Create(roots: [IndexerTestHarness.DefaultRoot]);
        await harness.Settings.SaveAsync(
            harness.Settings.Current with
            {
                Indexing = harness.Settings.Current.Indexing with
                {
                    Roots = [IndexerTestHarness.DefaultRoot, @"C:\GibtEsNicht"],
                },
            },
            CancellationToken.None).ConfigureAwait(true);

        await harness.Indexer.StartAsync(CancellationToken.None).ConfigureAwait(true);
        _ = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);

        _ = Assert.Single(harness.WatcherFactory.Watchers);
        Assert.Contains(IndexerTestHarness.DefaultRoot, harness.WatcherFactory.Watchers.Keys);

        await harness.Indexer.StopAsync(CancellationToken.None).ConfigureAwait(true);
    }

    [Fact]
    public async Task OnRenameWithoutOldPath_TreatsItAsANewFile()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        await harness.Indexer.StartAsync(CancellationToken.None).ConfigureAwait(true);
        _ = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);

        harness.FileSystem.AddFile(@"C:\Wurzel\ohne-vorgaenger.md", "Inhalt", FixedWrite);
        harness.WatcherFor(IndexerTestHarness.DefaultRoot).TriggerEvent(
            new FileSystemEvent(FileSystemEventKind.Renamed, @"C:\Wurzel\ohne-vorgaenger.md", OldPath: null, IndexerTestHarness.DefaultRoot));
        harness.TimeProvider.Advance(TimeSpan.FromMilliseconds(300));

        _ = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);
        Assert.Contains(@"C:\Wurzel\ohne-vorgaenger.md", harness.Repository.Snapshot.Keys);

        await harness.Indexer.StopAsync(CancellationToken.None).ConfigureAwait(true);
    }

    [Fact]
    public async Task OnRenameWithUnknownOldPath_TreatsItAsANewFile()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        await harness.Indexer.StartAsync(CancellationToken.None).ConfigureAwait(true);
        _ = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);

        harness.FileSystem.AddFile(@"C:\Wurzel\ziel.md", "Inhalt", FixedWrite);
        harness.WatcherFor(IndexerTestHarness.DefaultRoot).TriggerEvent(
            new FileSystemEvent(FileSystemEventKind.Renamed, @"C:\Wurzel\ziel.md", @"C:\Wurzel\nie-indiziert.md", IndexerTestHarness.DefaultRoot));
        harness.TimeProvider.Advance(TimeSpan.FromMilliseconds(300));

        _ = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);
        Assert.Contains(@"C:\Wurzel\ziel.md", harness.Repository.Snapshot.Keys);

        await harness.Indexer.StopAsync(CancellationToken.None).ConfigureAwait(true);
    }

    [Fact]
    public async Task OnRenameWhereTargetDoesNotExist_RemovesTheOldEntry()
    {
        // Umbenennung aus dem beobachteten Bereich heraus: Das Ziel liegt nicht mehr da,
        // wo der Indexer schaut — der alte Eintrag muss verschwinden statt zu verwaisen.
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\alt.md", "Inhalt", FixedWrite);

        await harness.Indexer.StartAsync(CancellationToken.None).ConfigureAwait(true);
        _ = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);
        Assert.Contains(@"C:\Wurzel\alt.md", harness.Repository.Snapshot.Keys);

        harness.FileSystem.RemoveFile(@"C:\Wurzel\alt.md");
        harness.WatcherFor(IndexerTestHarness.DefaultRoot).TriggerEvent(
            new FileSystemEvent(FileSystemEventKind.Renamed, @"C:\Woanders\alt.md", @"C:\Wurzel\alt.md", IndexerTestHarness.DefaultRoot));
        harness.TimeProvider.Advance(TimeSpan.FromMilliseconds(300));

        _ = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);
        Assert.DoesNotContain(@"C:\Wurzel\alt.md", harness.Repository.Snapshot.Keys);

        await harness.Indexer.StopAsync(CancellationToken.None).ConfigureAwait(true);
    }

    [Fact]
    public async Task OnChangeEventForMissingFile_DoesNotWrite()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        await harness.Indexer.StartAsync(CancellationToken.None).ConfigureAwait(true);
        _ = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);
        int vorher = harness.Repository.TotalWrites;

        harness.WatcherFor(IndexerTestHarness.DefaultRoot).TriggerEvent(
            new FileSystemEvent(FileSystemEventKind.Changed, @"C:\Wurzel\gibt-es-nicht.md", OldPath: null, IndexerTestHarness.DefaultRoot));
        harness.TimeProvider.Advance(TimeSpan.FromMilliseconds(300));
        await Task.Delay(100).ConfigureAwait(true);

        Assert.Equal(vorher, harness.Repository.TotalWrites);

        await harness.Indexer.StopAsync(CancellationToken.None).ConfigureAwait(true);
    }

    [Fact]
    public async Task OnChangeEventWithoutRealChange_DoesNotRewriteTheEntry()
    {
        // Gleiche Größe, gleicher Schreibzeitpunkt: Der Beobachter feuert trotzdem, etwa
        // beim Öffnen in einem Editor. Ein Schreibvorgang wäre hier reine Last.
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\gleichbleibend.md", "Immer gleich", FixedWrite);

        await harness.Indexer.StartAsync(CancellationToken.None).ConfigureAwait(true);
        _ = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);
        int vorher = harness.Repository.TotalWrites;

        harness.WatcherFor(IndexerTestHarness.DefaultRoot).TriggerEvent(
            new FileSystemEvent(FileSystemEventKind.Changed, @"C:\Wurzel\gleichbleibend.md", OldPath: null, IndexerTestHarness.DefaultRoot));
        harness.TimeProvider.Advance(TimeSpan.FromMilliseconds(300));
        await Task.Delay(100).ConfigureAwait(true);

        Assert.Equal(vorher, harness.Repository.TotalWrites);

        await harness.Indexer.StopAsync(CancellationToken.None).ConfigureAwait(true);
    }

    [Fact]
    public async Task OnTouchedFileWithSameContent_KeepsTheContentHash()
    {
        // Nur der Zeitstempel wandert (etwa durch ein Backup-Werkzeug), der Inhalt bleibt.
        // Der Eintrag muss aktualisiert werden, der Hash aber derselbe bleiben.
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\beruehrt.md", "Gleicher Inhalt", FixedWrite);

        await harness.Indexer.StartAsync(CancellationToken.None).ConfigureAwait(true);
        _ = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);
        MarkdownFile vorher = harness.Repository.Snapshot[@"C:\Wurzel\beruehrt.md"];
        string hashVorher = vorher.ContentHash;

        harness.FileSystem.UpdateFile(@"C:\Wurzel\beruehrt.md", "Gleicher Inhalt", FixedWrite.AddHours(1));
        harness.WatcherFor(IndexerTestHarness.DefaultRoot).TriggerEvent(
            new FileSystemEvent(FileSystemEventKind.Changed, @"C:\Wurzel\beruehrt.md", OldPath: null, IndexerTestHarness.DefaultRoot));
        harness.TimeProvider.Advance(TimeSpan.FromMilliseconds(300));
        _ = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);

        MarkdownFile nachher = harness.Repository.Snapshot[@"C:\Wurzel\beruehrt.md"];
        Assert.Equal(hashVorher, nachher.ContentHash, StringComparer.Ordinal);
        Assert.Equal(FixedWrite.AddHours(1), nachher.LastWriteTimeUtc);

        await harness.Indexer.StopAsync(CancellationToken.None).ConfigureAwait(true);
    }

    [Fact]
    public async Task OnDeleteEventForUnknownFile_DoesNotWrite()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        await harness.Indexer.StartAsync(CancellationToken.None).ConfigureAwait(true);
        _ = await harness.Repository.WaitForNextSaveAsync(Timeout).ConfigureAwait(true);
        int vorher = harness.Repository.TotalWrites;

        harness.WatcherFor(IndexerTestHarness.DefaultRoot).TriggerEvent(
            new FileSystemEvent(FileSystemEventKind.Deleted, @"C:\Wurzel\war-nie-da.md", OldPath: null, IndexerTestHarness.DefaultRoot));
        harness.TimeProvider.Advance(TimeSpan.FromMilliseconds(300));
        await Task.Delay(100).ConfigureAwait(true);

        Assert.Equal(vorher, harness.Repository.TotalWrites);

        await harness.Indexer.StopAsync(CancellationToken.None).ConfigureAwait(true);
    }
}
