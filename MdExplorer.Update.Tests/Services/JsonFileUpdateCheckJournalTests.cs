using MdExplorer.Update.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.Update.Tests.Services;

/// <summary>Tests für die Datei-Persistenz von <see cref="JsonFileUpdateCheckJournal"/>.</summary>
public sealed class JsonFileUpdateCheckJournalTests : IDisposable
{
    private readonly string _filePath = Path.Combine(
        Path.GetTempPath(),
        $"mdexplorer-update-journal-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task ReadLastCheck_WhenFileMissing_ReturnsNull()
    {
        JsonFileUpdateCheckJournal journal = Create();

        DateTimeOffset? result = await journal.ReadLastCheckAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task WriteThenRead_RoundTripsTimestamp()
    {
        JsonFileUpdateCheckJournal journal = Create();
        DateTimeOffset timestamp = new(2026, 6, 26, 8, 30, 0, TimeSpan.Zero);

        await journal.WriteLastCheckAsync(timestamp, CancellationToken.None);
        DateTimeOffset? result = await journal.ReadLastCheckAsync(CancellationToken.None);

        Assert.Equal(timestamp, result);
    }

    [Fact]
    public async Task ReadLastCheck_WhenFileCorrupt_ReturnsNull()
    {
        await File.WriteAllTextAsync(_filePath, "{ this is not valid json");
        JsonFileUpdateCheckJournal journal = Create();

        DateTimeOffset? result = await journal.ReadLastCheckAsync(CancellationToken.None);

        Assert.Null(result);
    }

    /// <summary>Entfernt die temporäre Journal-Datei.</summary>
    public void Dispose()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    private JsonFileUpdateCheckJournal Create() =>
        new(_filePath, NullLogger<JsonFileUpdateCheckJournal>.Instance);
}
