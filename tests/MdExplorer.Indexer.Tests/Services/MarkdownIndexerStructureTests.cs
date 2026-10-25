using MdExplorer.Core.Models;
using MdExplorer.Core.Settings;
using MdExplorer.Indexer.Models;
using MdExplorer.Indexer.Services;
using MdExplorer.Indexer.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.Indexer.Tests.Services;

/// <summary>
/// Ereignisse, die viele Dateien auf einmal betreffen: ein umbenannter oder gelöschter Ordner und
/// eine geänderte <c>.mdignore</c>. Für die Dateien in einem Ordner meldet der Watcher dabei nichts
/// einzeln; ohne eigene Behandlung blieben alte Pfade bis zum Abgleich stehen und bekämen danach
/// neue Ids, oder eine neue Ausschlussregel wirkte erst nach einem Neustart.
/// </summary>
public sealed class MarkdownIndexerStructureTests
{
    private const string Root = IndexerTestHarness.DefaultRoot;
    private static readonly DateTime FixedWrite = new(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task OnARenamedFolder_MovesItsEntriesAndKeepsTheirIds()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\projekte\a.md", "A", FixedWrite);
        harness.FileSystem.AddFile(@"C:\Wurzel\projekte\tief\b.md", "B", FixedWrite);
        harness.FileSystem.AddFile(@"C:\Wurzel\projekte-alt\c.md", "C", FixedWrite);
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);
        Dictionary<string, Guid> idsBefore = harness.Store.Snapshot.ToDictionary(entry => entry.Value.RelativePath, entry => entry.Value.Id);

        harness.FileSystem.MoveDirectory(@"C:\Wurzel\projekte", @"C:\Wurzel\archiv");
        await harness.ProcessAsync(new FileSystemEvent(FileSystemEventKind.DirectoryRenamed, @"C:\Wurzel\archiv", @"C:\Wurzel\projekte", Root)).ConfigureAwait(true);

        IReadOnlyDictionary<string, MarkdownFile> after = harness.Store.Snapshot;
        Assert.Equal(idsBefore[@"projekte\a.md"], after[@"C:\Wurzel\archiv\a.md"].Id);
        Assert.Equal(idsBefore[@"projekte\tief\b.md"], after[@"C:\Wurzel\archiv\tief\b.md"].Id);
        Assert.Equal(@"archiv\tief\b.md", after[@"C:\Wurzel\archiv\tief\b.md"].RelativePath);
        Assert.Equal(idsBefore[@"projekte-alt\c.md"], after[@"C:\Wurzel\projekte-alt\c.md"].Id);
    }

    [Fact]
    public async Task OnADeletedFolder_RemovesItsEntriesOnly()
    {
        await using IndexerTestHarness harness = IndexerTestHarness.Create();
        harness.FileSystem.AddFile(@"C:\Wurzel\weg\a.md", "A", FixedWrite);
        harness.FileSystem.AddFile(@"C:\Wurzel\weg\tief\b.md", "B", FixedWrite);
        harness.FileSystem.AddFile(@"C:\Wurzel\weg-nicht\c.md", "C", FixedWrite);
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);

        harness.FileSystem.RemoveDirectory(@"C:\Wurzel\weg");
        await harness.ProcessAsync(new FileSystemEvent(FileSystemEventKind.DirectoryDeleted, @"C:\Wurzel\weg", OldPath: null, Root)).ConfigureAwait(true);

        Assert.Equal([@"C:\Wurzel\weg-nicht\c.md"], harness.Store.Snapshot.Keys);
    }

    [Fact]
    public async Task OnAChangedMdIgnore_AppliesTheNewRuleWithoutARestart()
    {
        // Wer eine .mdignore bearbeitet, erwartet die Wirkung sofort. Bisher galt die alte Regel bis
        // zur nächsten Einstellungsänderung oder zum Neustart.
        FakeFileSystem sharedFileSystem = new();
        FakeSettingsService settings = new(AppSettings.Default with
        {
            Indexing = AppSettings.Default.Indexing with { Roots = [Root], ExclusionPatterns = [] },
        });
        using GlobExclusionFilter filter = new(settings, new MdIgnoreHierarchy(new MdIgnoreReader(sharedFileSystem)), NullLogger<GlobExclusionFilter>.Instance);
        await using IndexerTestHarness harness = IndexerTestHarness.Create(exclusionFilter: filter, fileSystem: sharedFileSystem);
        harness.FileSystem.AddFile(@"C:\Wurzel\entwurf\a.md", "A", FixedWrite);
        harness.FileSystem.AddFile(@"C:\Wurzel\b.md", "B", FixedWrite);
        await harness.StartAndWaitForInitialScanAsync().ConfigureAwait(true);
        Assert.Equal(2, harness.Store.Snapshot.Count);

        harness.FileSystem.AddFile(@"C:\Wurzel\entwurf\.mdignore", "*\n", FixedWrite);
        harness.WatcherFor(Root).TriggerEvent(new FileSystemEvent(FileSystemEventKind.IgnoreFileChanged, @"C:\Wurzel\entwurf\.mdignore", OldPath: null, Root));

        await harness.WaitUntilAsync(() => harness.Store.Snapshot.Count == 1, "Die neue Ausschlussregel wirkte nicht.").ConfigureAwait(true);
        Assert.Equal([@"C:\Wurzel\b.md"], harness.Store.Snapshot.Keys);
    }
}
