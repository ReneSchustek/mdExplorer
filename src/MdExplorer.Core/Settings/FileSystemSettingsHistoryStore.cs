using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Core.Settings;

/// <summary>
/// Persistiert Settings-Snapshots und Audit-Log-Einträge über <see cref="IFileSystem"/>.
/// Im Betrieb übergibt die DI-Registrierung die Pfade aus <see cref="AppPaths"/>; Tests geben
/// eigene Pfade und einen eigenen Retention-Wert vor.
/// </summary>
public sealed partial class FileSystemSettingsHistoryStore : ISettingsHistoryStore, IDisposable
{
    /// <summary>
    /// Wie viele Snapshot-Dateien höchstens liegen bleiben. Die Snapshots dienen nur dem
    /// Zurückholen eines jüngeren Stands.
    /// </summary>
    public const int DefaultRetention = 30;

    /// <summary>
    /// Ab dieser Größe wird die Audit-Datei zur Vorgängerdatei. Eine Zeile hat wenige hundert
    /// Byte; ein Megabyte fasst damit einige tausend Änderungen, also Jahre bei üblicher Nutzung,
    /// und das Lesen der Datei beim Anhängen bleibt billig.
    /// </summary>
    public const long DefaultAuditRotationBytes = 1024 * 1024;

    // Umlaute und Pfade bleiben in der Datei lesbar; <, > und & werden trotzdem maskiert. Der
    // gelockerte Encoder ließe sie stehen, und eine Zeile, die einmal in HTML landet, wäre dann
    // ein Einfallstor.
    private static readonly JsonSerializerOptions AuditOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _historyDirectory;
    private readonly string _auditLogPath;
    private readonly int _retention;
    private readonly long _auditRotationBytes;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<FileSystemSettingsHistoryStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    /// <summary>Erstellt den Store mit expliziten Pfaden und Grenzen.</summary>
    /// <param name="historyDirectory">Verzeichnis der Snapshots.</param>
    /// <param name="auditLogPath">Pfad der Audit-Datei.</param>
    /// <param name="retention">Höchstzahl liegen bleibender Snapshots.</param>
    /// <param name="auditRotationBytes">Größe, ab der die Audit-Datei rotiert wird.</param>
    /// <param name="fileSystem">Dateisystem.</param>
    /// <param name="logger">Protokoll.</param>
    public FileSystemSettingsHistoryStore(
        string historyDirectory,
        string auditLogPath,
        int retention,
        long auditRotationBytes,
        IFileSystem fileSystem,
        ILogger<FileSystemSettingsHistoryStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(auditLogPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retention);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(auditRotationBytes);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        _historyDirectory = historyDirectory;
        _auditLogPath = auditLogPath;
        _retention = retention;
        _auditRotationBytes = auditRotationBytes;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>Pfad der Vorgängerdatei, in die die Audit-Datei beim Rotieren wandert.</summary>
    public string PreviousAuditLogPath => _auditLogPath + ".1";

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
            string snapshotName = await WriteSnapshotAsync(currentJson, timestamp, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<SettingsChangeEntry> changes = SettingsDiff.Compute(previousJson, currentJson);
            await AppendAuditEntryAsync(timestamp, snapshotName, changes, cancellationToken).ConfigureAwait(false);
            EnforceRetention();
        }
        finally
        {
            _ = _writeLock.Release();
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

    [LoggerMessage(EventId = 610, Level = LogLevel.Information, Message = "Settings-Snapshot geschrieben: {Path}")]
    private static partial void LogSnapshotWritten(ILogger logger, string path);

    [LoggerMessage(EventId = 611, Level = LogLevel.Information, Message = "Settings-Audit ergänzt ({ChangeCount} Änderungen): {Path}")]
    private static partial void LogAuditAppended(ILogger logger, string path, int changeCount);

    [LoggerMessage(EventId = 612, Level = LogLevel.Warning, Message = "Settings-Snapshot konnte nicht gelöscht werden: {Path}")]
    private static partial void LogRetentionFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 614, Level = LogLevel.Information, Message = "Settings-Audit rotiert nach {PreviousPath}.")]
    private static partial void LogAuditRotated(ILogger logger, string previousPath);

    private async Task<string> WriteSnapshotAsync(string currentJson, DateTimeOffset timestamp, CancellationToken cancellationToken)
    {
        string snapshotName = "settings."
            + timestamp.UtcDateTime.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture)
            + ".json";
        string snapshotPath = Path.Combine(_historyDirectory, snapshotName);
        await _fileSystem.WriteAllBytesAtomicAsync(snapshotPath, Utf8NoBom.GetBytes(currentJson), cancellationToken).ConfigureAwait(false);
        LogSnapshotWritten(_logger, snapshotPath);
        return snapshotName;
    }

    // Die Datei wird ganz neu geschrieben statt angehängt: So bleibt auch die Audit-Datei atomar,
    // und ein Absturz mitten im Schreiben hinterlässt keine halbe Zeile. Die Rotation hält den
    // Preis dafür klein.
    private async Task AppendAuditEntryAsync(
        DateTimeOffset timestamp,
        string snapshotName,
        IReadOnlyList<SettingsChangeEntry> changes,
        CancellationToken cancellationToken)
    {
        RotateAuditIfFull();
        byte[] existing = _fileSystem.FileExists(_auditLogPath)
            ? await _fileSystem.ReadAllBytesAsync(_auditLogPath, cancellationToken).ConfigureAwait(false)
            : [];
        AuditEntry entry = new(timestamp.UtcDateTime.ToString("o", CultureInfo.InvariantCulture), snapshotName, changes);
        byte[] line = Utf8NoBom.GetBytes(JsonSerializer.Serialize(entry, AuditOptions) + Environment.NewLine);
        byte[] content = [.. existing, .. line];
        await _fileSystem.WriteAllBytesAtomicAsync(_auditLogPath, content, cancellationToken).ConfigureAwait(false);
        LogAuditAppended(_logger, _auditLogPath, changes.Count);
    }

    private void RotateAuditIfFull()
    {
        if (!_fileSystem.FileExists(_auditLogPath) || _fileSystem.GetFileSize(_auditLogPath) < _auditRotationBytes)
        {
            return;
        }
        _fileSystem.DeleteFile(PreviousAuditLogPath);
        _fileSystem.MoveFile(_auditLogPath, PreviousAuditLogPath);
        LogAuditRotated(_logger, PreviousAuditLogPath);
    }

    private void EnforceRetention()
    {
        List<string> snapshots = [.. _fileSystem.EnumerateFiles(_historyDirectory, "settings.*.json", recursive: false)];
        if (snapshots.Count <= _retention)
        {
            return;
        }
        // Der Zeitstempel im Dateinamen hat feste Breite und steht vorn; die Textsortierung ist
        // damit zugleich die zeitliche, und die ältesten Dateien stehen am Anfang.
        snapshots.Sort(StringComparer.Ordinal);
        foreach (string snapshot in snapshots.Take(snapshots.Count - _retention))
        {
            try
            {
                _fileSystem.DeleteFile(snapshot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogRetentionFailed(_logger, snapshot, ex);
            }
        }
    }

    /// <summary>Eine Zeile der Audit-Datei: wann, welcher Snapshot, welche Felder sich geändert haben.</summary>
    private sealed record AuditEntry(
        string Timestamp,
        string Snapshot,
        IReadOnlyList<SettingsChangeEntry> Changes);
}
