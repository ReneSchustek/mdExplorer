using System.Globalization;
using System.Text;
using System.Text.Json;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Core.Settings;

/// <summary>
/// Persistiert Settings-Snapshots und Audit-Log-Einträge im Dateisystem.
/// Default-Pfade kommen aus <see cref="AppPaths"/>; für Tests können die Pfade
/// und der Retention-Wert explizit gesetzt werden.
/// </summary>
public sealed partial class FileSystemSettingsHistoryStore : ISettingsHistoryStore, IDisposable
{
    /// <summary>Default-Retention für Snapshot-Dateien (vgl. Log-Rotation in <c>logging.md</c>).</summary>
    public const int DefaultRetention = 30;

    private static readonly JsonSerializerOptions AuditOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _historyDirectory;
    private readonly string _auditLogPath;
    private readonly int _retention;
    private readonly ILogger<FileSystemSettingsHistoryStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    /// <summary>Erstellt den Store mit expliziten Pfaden und Retention.</summary>
    public FileSystemSettingsHistoryStore(
        string historyDirectory,
        string auditLogPath,
        int retention,
        ILogger<FileSystemSettingsHistoryStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(auditLogPath);
        ArgumentNullException.ThrowIfNull(logger);
        if (retention <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retention), retention, "Retention muss > 0 sein.");
        }
        _historyDirectory = historyDirectory;
        _auditLogPath = auditLogPath;
        _retention = retention;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RecordAsync(
        AppSettings previous,
        AppSettings current,
        string previousJson,
        string currentJson,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrEmpty(previousJson);
        ArgumentException.ThrowIfNullOrEmpty(currentJson);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _ = Directory.CreateDirectory(_historyDirectory);
            string snapshotName = "settings."
                + timestamp.UtcDateTime.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture)
                + ".json";
            string snapshotPath = Path.Combine(_historyDirectory, snapshotName);
            await File.WriteAllTextAsync(snapshotPath, currentJson, new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);
            LogSnapshotWritten(_logger, snapshotPath);

            IReadOnlyList<SettingsChangeEntry> changes = SettingsDiff.Compute(previousJson, currentJson);
            await AppendAuditEntryAsync(timestamp, snapshotName, changes, cancellationToken).ConfigureAwait(false);

            EnforceRetention();
        }
        finally
        {
            _ = _writeLock.Release();
        }
    }

    private async Task AppendAuditEntryAsync(
        DateTimeOffset timestamp,
        string snapshotName,
        IReadOnlyList<SettingsChangeEntry> changes,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_auditLogPath);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }
        AuditEntry entry = new(
            timestamp.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            snapshotName,
            changes);
        string line = JsonSerializer.Serialize(entry, AuditOptions);
        await File.AppendAllTextAsync(
                _auditLogPath,
                line + Environment.NewLine,
                new UTF8Encoding(false),
                cancellationToken)
            .ConfigureAwait(false);
        LogAuditAppended(_logger, _auditLogPath, changes.Count);
    }

    private void EnforceRetention()
    {
        string[] snapshots = Directory.GetFiles(_historyDirectory, "settings.*.json");
        if (snapshots.Length <= _retention)
        {
            return;
        }
        Array.Sort(snapshots, StringComparer.Ordinal);
        int excess = snapshots.Length - _retention;
        for (int i = 0; i < excess; i++)
        {
            try
            {
                File.Delete(snapshots[i]);
            }
            catch (IOException ex)
            {
                LogRetentionFailed(_logger, snapshots[i], ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                LogRetentionFailed(_logger, snapshots[i], ex);
            }
        }
    }

    /// <summary>Gibt das interne <see cref="SemaphoreSlim"/> frei.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _writeLock.Dispose();
    }

    private sealed record AuditEntry(
        string Timestamp,
        string Snapshot,
        IReadOnlyList<SettingsChangeEntry> Changes);

    [LoggerMessage(EventId = 610, Level = LogLevel.Information, Message = "Settings-Snapshot geschrieben: {Path}")]
    private static partial void LogSnapshotWritten(ILogger logger, string path);

    [LoggerMessage(EventId = 611, Level = LogLevel.Information, Message = "Settings-Audit appended ({ChangeCount} Änderungen): {Path}")]
    private static partial void LogAuditAppended(ILogger logger, string path, int changeCount);

    [LoggerMessage(EventId = 612, Level = LogLevel.Warning, Message = "Settings-Snapshot konnte nicht gelöscht werden: {Path}")]
    private static partial void LogRetentionFailed(ILogger logger, string path, Exception exception);
}
