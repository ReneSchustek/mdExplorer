using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Settings;
using MdExplorer.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.Core.Tests.Settings;

/// <summary>
/// Baut den Einstellungsdienst mit Dateisystem im Speicher und fester Uhr für die Tests.
/// </summary>
internal static class SettingsServiceFactory
{
    public const string SettingsPath = @"C:\Daten\MdExplorer\settings.json";

    public const string HistoryDirectory = @"C:\Daten\MdExplorer\settings-history";

    public const string AuditPath = @"C:\Daten\MdExplorer\settings-audit.log";

    public static readonly DateTimeOffset Start = new(2026, 06, 10, 12, 0, 0, TimeSpan.Zero);

    public static JsonSettingsService Create(
        InMemoryFileSystem fileSystem,
        ISettingsHistoryStore? history = null,
        FakeTimeProvider? clock = null,
        Microsoft.Extensions.Logging.ILogger<JsonSettingsService>? logger = null) =>
        new(SettingsPath, fileSystem, logger ?? NullLogger<JsonSettingsService>.Instance, history, clock ?? new FakeTimeProvider(Start));

    public static FileSystemSettingsHistoryStore CreateHistory(InMemoryFileSystem fileSystem, int retention = FileSystemSettingsHistoryStore.DefaultRetention) =>
        new(
            HistoryDirectory,
            AuditPath,
            retention,
            FileSystemSettingsHistoryStore.DefaultAuditRotationBytes,
            fileSystem,
            NullLogger<FileSystemSettingsHistoryStore>.Instance);
}
