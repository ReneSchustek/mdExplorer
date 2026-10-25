using MdExplorer.Core.FileSystem;
using MdExplorer.Update.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.Update.Tests.Services;

/// <summary>
/// <see cref="JsonFileUpdateCheckJournal"/> schreibt den Zeitpunkt der letzten Update-Prüfung in eine
/// Datei und liest ihn zurück; fehlt die Datei oder ist sie kaputt, gilt das als „noch nie geprüft".
/// Würfe das Lesen stattdessen, fände gar keine Prüfung mehr statt. Ein Schreibfehler kostet
/// höchstens eine Prüfung mehr und darf ebenfalls nicht werfen.
/// </summary>
public sealed class JsonFileUpdateCheckJournalTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("MdExplorer.Tests-");

    private string FilePath => Path.Combine(_directory.FullName, "update-check.json");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public async Task ReadLastCheck_WhenFileMissing_ReturnsNull()
    {
        JsonFileUpdateCheckJournal journal = Create(FilePath);

        DateTimeOffset? result = await journal.ReadLastCheckAsync(TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task WriteThenRead_RoundTripsTimestamp()
    {
        JsonFileUpdateCheckJournal journal = Create(FilePath);
        DateTimeOffset timestamp = new(2026, 6, 26, 8, 30, 0, TimeSpan.Zero);

        await journal.WriteLastCheckAsync(timestamp, TestContext.Current.CancellationToken);
        DateTimeOffset? result = await journal.ReadLastCheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(timestamp, result);
        Assert.Equal([FilePath], Directory.GetFiles(_directory.FullName));
    }

    [Fact]
    public async Task ReadLastCheck_WhenFileCorrupt_ReturnsNull()
    {
        await File.WriteAllTextAsync(FilePath, "{ this is not valid json", TestContext.Current.CancellationToken);
        JsonFileUpdateCheckJournal journal = Create(FilePath);

        DateTimeOffset? result = await journal.ReadLastCheckAsync(TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task WriteLastCheck_WhenTheFolderIsBlocked_DoesNotThrow()
    {
        // Eine Datei an der Stelle des Ordners: Das Anlegen scheitert mit einer IOException.
        string blocked = Path.Combine(_directory.FullName, "blockiert");
        await File.WriteAllTextAsync(blocked, "keine Mappe", TestContext.Current.CancellationToken);
        JsonFileUpdateCheckJournal journal = Create(Path.Combine(blocked, "update-check.json"));

        await journal.WriteLastCheckAsync(new DateTimeOffset(2026, 6, 26, 8, 30, 0, TimeSpan.Zero), TestContext.Current.CancellationToken);

        Assert.Equal([blocked], Directory.GetFiles(_directory.FullName));
    }

    [Fact]
    public async Task WriteLastCheck_WhenCancelled_ThrowsAndKeepsThePreviousState()
    {
        JsonFileUpdateCheckJournal journal = Create(FilePath);
        DateTimeOffset earlier = new(2026, 6, 25, 8, 0, 0, TimeSpan.Zero);
        await journal.WriteLastCheckAsync(earlier, TestContext.Current.CancellationToken);
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => journal.WriteLastCheckAsync(earlier.AddDays(1), cts.Token));

        Assert.Equal(earlier, await journal.ReadLastCheckAsync(TestContext.Current.CancellationToken));
        Assert.Equal([FilePath], Directory.GetFiles(_directory.FullName));
    }

    private static JsonFileUpdateCheckJournal Create(string filePath) =>
        new(filePath, new LocalFileSystem(), NullLogger<JsonFileUpdateCheckJournal>.Instance);
}
