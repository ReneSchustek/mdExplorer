namespace MdExplorer.Search.Tests.Integration;

/// <summary>
/// Der Prüfstand räumt seine Datenbank nach jedem Test vollständig weg. Blieben Datei und
/// Begleitdateien liegen, füllte jeder Testlauf den Temp-Ordner um ein paar Dutzend Datenbanken.
/// </summary>
public sealed class SearchTestHarnessTests
{
    [Fact]
    public async Task DisposeAsync_AfterUse_LeavesNoDatabaseFilesBehind()
    {
        string databasePath;
        SearchTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            databasePath = harness.DatabasePath;
            await harness.SeedAsync(
                new SeedDocument(
                    new Guid("11111111-1111-1111-1111-111111111111"),
                    Title: "Notiz",
                    AbsolutePath: @"C:\Wurzel\notiz.md",
                    RelativePath: "notiz.md",
                    SourceContentHash: "hash-1",
                    FrontmatterJson: "{}",
                    Body: "Text",
                    RawSource: "Text",
                    Tags: []),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            _ = await harness.Maintainer.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        }

        Assert.False(File.Exists(databasePath), databasePath);
        Assert.False(File.Exists(databasePath + "-wal"), databasePath + "-wal");
        Assert.False(File.Exists(databasePath + "-shm"), databasePath + "-shm");
    }
}
