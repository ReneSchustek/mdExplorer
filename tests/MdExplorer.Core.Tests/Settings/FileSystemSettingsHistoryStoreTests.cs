using System.Text.Json;
using MdExplorer.Core.Models;
using MdExplorer.Core.Settings;
using MdExplorer.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using static MdExplorer.Core.Tests.Settings.SettingsServiceFactory;

namespace MdExplorer.Core.Tests.Settings;

/// <summary>
/// Jede gespeicherte Einstellung hinterlässt einen Schnappschuss und eine Zeile im
/// Änderungsprotokoll; nur die jüngsten Schnappschüsse bleiben, und das Protokoll wächst nicht
/// ohne Grenze. Ein gesperrter alter Schnappschuss darf das Schreiben des neuen nicht verhindern.
/// </summary>
public sealed class FileSystemSettingsHistoryStoreTests
{
    private static readonly DateTimeOffset Noon = new(2026, 06, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_WithNonPositiveRetention_Throws()
    {
        // Retention 0 würde jeden gerade geschriebenen Schnappschuss sofort wieder löschen;
        // die Historie wäre dauerhaft leer, ohne dass es auffiele.
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CreateHistory(new InMemoryFileSystem(), retention: 0));
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        FileSystemSettingsHistoryStore sut = CreateHistory(new InMemoryFileSystem());

        sut.Dispose();
        sut.Dispose();

        Assert.EndsWith(".1", sut.PreviousAuditLogPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordAsync_WritesSnapshotAndAuditLine()
    {
        InMemoryFileSystem fileSystem = new();
        using FileSystemSettingsHistoryStore sut = CreateHistory(fileSystem);

        await RecordDebounceAsync(sut, 300, 999, new DateTimeOffset(2026, 06, 10, 12, 34, 56, 789, TimeSpan.Zero)).ConfigureAwait(true);

        string snapshot = Assert.Single(fileSystem.EnumerateFiles(HistoryDirectory, "settings.*.json", recursive: false));
        Assert.Contains("20260610T123456789", Path.GetFileName(snapshot), StringComparison.Ordinal);
        string line = Assert.Single(fileSystem.Text(AuditPath).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("\"behavior.searchDebounceMs\"", line, StringComparison.Ordinal);
        Assert.Contains("\"300\"", line, StringComparison.Ordinal);
        Assert.Contains("\"999\"", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordAsync_KeepsUmlautsReadableButMasksMarkup()
    {
        // Der gelockerte Encoder ließ <, > und & stehen. Pfade mit Umlauten sollen lesbar bleiben,
        // ein eingefügtes <script> nicht.
        InMemoryFileSystem fileSystem = new();
        using FileSystemSettingsHistoryStore sut = CreateHistory(fileSystem);
        AppSettings before = AppSettings.Default;
        AppSettings after = before with { Indexing = before.Indexing with { Roots = [@"C:\Notizen\Bücher<script>"] } };

        await sut.RecordAsync(before, after, Json(before), Json(after), Noon, TestContext.Current.CancellationToken).ConfigureAwait(true);

        string line = fileSystem.Text(AuditPath);
        Assert.Contains("Bücher", line, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordAsync_WhenAnOldSnapshotIsLocked_KeepsWritingTheNewOne()
    {
        // Ein Virenscanner oder ein offener Editor kann einen alten Schnappschuss halten. Mit einer
        // Aufbewahrung von eins versucht schon der zweite Stand, den ersten zu löschen.
        InMemoryFileSystem fileSystem = new();
        using FileSystemSettingsHistoryStore sut = CreateHistory(fileSystem, retention: 1);
        await RecordDebounceAsync(sut, 300, 111, Noon).ConfigureAwait(true);
        string oldSnapshot = Assert.Single(fileSystem.EnumerateFiles(HistoryDirectory, "settings.*.json", recursive: false));
        fileSystem.FailDeletesOf(oldSnapshot, new IOException("gesperrt"));

        await RecordDebounceAsync(sut, 111, 222, Noon.AddMinutes(5)).ConfigureAwait(true);

        Assert.Equal(2, fileSystem.EnumerateFiles(HistoryDirectory, "settings.*.json", recursive: false).Count());
    }

    [Fact]
    public async Task RecordAsync_OnExceedingRetention_KeepsOnlyMostRecent()
    {
        // Drei statt der dreißig ab Werk: Fünf Schreibvorgänge schieben zwei über die Grenze.
        const int retention = 3;
        InMemoryFileSystem fileSystem = new();
        using FileSystemSettingsHistoryStore sut = CreateHistory(fileSystem, retention);

        for (int i = 0; i < retention + 2; i++)
        {
            await RecordDebounceAsync(sut, 100 + i, 101 + i, Noon.AddMilliseconds(i)).ConfigureAwait(true);
        }

        List<string> snapshots = [.. fileSystem.EnumerateFiles(HistoryDirectory, "settings.*.json", recursive: false).Order(StringComparer.Ordinal)];
        Assert.Equal(retention, snapshots.Count);
        Assert.Contains("20260610T120000002", Path.GetFileName(snapshots[0]), StringComparison.Ordinal);
        Assert.Contains("20260610T120000004", Path.GetFileName(snapshots[^1]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordAsync_WhenTheAuditReachesItsLimit_StartsAFreshFileAndKeepsThePreviousOne()
    {
        // Ohne Grenze wüchse die Datei über Jahre, und jedes Anhängen läse sie ganz.
        InMemoryFileSystem fileSystem = new();
        using FileSystemSettingsHistoryStore sut = new(
            HistoryDirectory,
            AuditPath,
            FileSystemSettingsHistoryStore.DefaultRetention,
            auditRotationBytes: 10,
            fileSystem,
            NullLogger<FileSystemSettingsHistoryStore>.Instance);

        await RecordDebounceAsync(sut, 300, 400, Noon).ConfigureAwait(true);
        await RecordDebounceAsync(sut, 400, 500, Noon.AddSeconds(1)).ConfigureAwait(true);

        Assert.Contains("\"400\"", fileSystem.Text(sut.PreviousAuditLogPath), StringComparison.Ordinal);
        string current = Assert.Single(fileSystem.Text(AuditPath).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("\"500\"", current, StringComparison.Ordinal);
    }

    private static string Json(AppSettings settings) => JsonSerializer.Serialize(settings, SettingsJson.Options);

    private static Task RecordDebounceAsync(FileSystemSettingsHistoryStore sut, int from, int to, DateTimeOffset timestamp)
    {
        AppSettings before = AppSettings.Default with { Behavior = AppSettings.Default.Behavior with { SearchDebounceMs = from } };
        AppSettings after = before with { Behavior = before.Behavior with { SearchDebounceMs = to } };
        return sut.RecordAsync(before, after, Json(before), Json(after), timestamp, TestContext.Current.CancellationToken);
    }
}
