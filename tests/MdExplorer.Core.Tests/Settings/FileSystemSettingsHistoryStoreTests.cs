using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MdExplorer.Core.Models;
using MdExplorer.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.Core.Tests.Settings;

public sealed class FileSystemSettingsHistoryStoreTests : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _tempDir;
    private readonly string _historyDir;
    private readonly string _auditPath;

    public FileSystemSettingsHistoryStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mdexp-history-" + Guid.NewGuid().ToString("N"));
        _historyDir = Path.Combine(_tempDir, "settings-history");
        _auditPath = Path.Combine(_tempDir, "settings-audit.log");
        _ = Directory.CreateDirectory(_historyDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public async Task RecordAsync_WritesSnapshotAndAuditLine()
    {
        using FileSystemSettingsHistoryStore sut = new(
            _historyDir,
            _auditPath,
            FileSystemSettingsHistoryStore.DefaultRetention,
            NullLogger<FileSystemSettingsHistoryStore>.Instance);
        AppSettings previous = AppSettings.Default;
        AppSettings current = previous with
        {
            Behavior = previous.Behavior with { SearchDebounceMs = 999 },
        };
        DateTimeOffset timestamp = new(2026, 06, 10, 12, 34, 56, 789, TimeSpan.Zero);

        await sut.RecordAsync(
                previous,
                current,
                JsonSerializer.Serialize(previous, SerializerOptions),
                JsonSerializer.Serialize(current, SerializerOptions),
                timestamp,
                CancellationToken.None)
            .ConfigureAwait(true);

        string[] snapshots = Directory.GetFiles(_historyDir, "settings.*.json");
        string snapshot = Assert.Single(snapshots);
        Assert.EndsWith(".json", snapshot, StringComparison.Ordinal);
        Assert.Contains("20260610T123456789", Path.GetFileName(snapshot), StringComparison.Ordinal);

        string[] auditLines = await File.ReadAllLinesAsync(_auditPath).ConfigureAwait(true);
        string line = Assert.Single(auditLines);
        Assert.Contains("\"behavior.searchDebounceMs\"", line, StringComparison.Ordinal);
        Assert.Contains("\"300\"", line, StringComparison.Ordinal);
        Assert.Contains("\"999\"", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordAsync_OnExceedingRetention_KeepsOnlyMostRecent()
    {
        const int retention = 3;
        using FileSystemSettingsHistoryStore sut = new(
            _historyDir,
            _auditPath,
            retention,
            NullLogger<FileSystemSettingsHistoryStore>.Instance);

        AppSettings previous = AppSettings.Default;
        for (int i = 0; i < retention + 2; i++)
        {
            AppSettings current = previous with
            {
                Behavior = previous.Behavior with { SearchDebounceMs = 100 + i },
            };
            DateTimeOffset timestamp = new(2026, 06, 10, 12, 0, 0, i, TimeSpan.Zero);
            await sut.RecordAsync(
                    previous,
                    current,
                    JsonSerializer.Serialize(previous, SerializerOptions),
                    JsonSerializer.Serialize(current, SerializerOptions),
                    timestamp,
                    CancellationToken.None)
                .ConfigureAwait(true);
            previous = current;
        }

        string[] snapshots = Directory.GetFiles(_historyDir, "settings.*.json");
        Assert.Equal(retention, snapshots.Length);
        // Älteste zwei (Index 0 und 1 nach Sortierung) sind weg, die zuletzt geschriebenen drei bleiben.
        Array.Sort(snapshots, StringComparer.Ordinal);
        Assert.Contains("20260610T120000002", Path.GetFileName(snapshots[0]), StringComparison.Ordinal);
        Assert.Contains("20260610T120000004", Path.GetFileName(snapshots[^1]), StringComparison.Ordinal);
    }
}
