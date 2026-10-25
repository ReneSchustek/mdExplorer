using System.IO;

namespace MdExplorer.TagCloud.Tests.Services;

/// <summary>
/// Die Testumgebung räumt ihre Datenbank wieder ab, samt Journal-Dateien. Hielte der
/// Verbindungs-Pool die Datei offen, scheiterte das Löschen still, und mit jedem Lauf blieben
/// weitere Datenbanken im Temp-Ordner liegen.
/// </summary>
public sealed class TagStatisticsTestHarnessTests
{
    private static readonly DateTime BaseTime = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task DisposeAsync_RemovesTheDatabaseAndItsJournalFiles()
    {
        TagStatisticsTestHarness harness = new();
        string databasePath = harness.DatabasePath;
        // Der große Bestand schaltet auf WAL; dabei entstehen -wal und -shm neben der Datenbank.
        await harness.SeedLargeDatasetAsync(documentCount: 10, distinctTagCount: 5, BaseTime).ConfigureAwait(true);
        _ = await harness.Service.GetTopTagsAsync(5, TestContext.Current.CancellationToken).ConfigureAwait(true);

        await harness.DisposeAsync().ConfigureAwait(true);

        Assert.False(File.Exists(databasePath), databasePath);
        Assert.False(File.Exists(databasePath + "-wal"), databasePath + "-wal");
        Assert.False(File.Exists(databasePath + "-shm"), databasePath + "-shm");
    }
}
