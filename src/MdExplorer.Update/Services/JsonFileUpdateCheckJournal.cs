using System.Text.Json;
using System.Text.Json.Serialization;
using MdExplorer.Core.Abstractions;
using MdExplorer.Update.Abstractions;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Update.Services;

/// <summary>
/// Datei-basierte Implementierung von <see cref="IUpdateCheckJournal"/>. Persistiert den
/// Zeitstempel der letzten Prüfung als kleine JSON-Datei und schreibt atomar über
/// <see cref="IFileSystem.WriteAllBytesAtomicAsync"/>. Lese- und Schreibfehler landen nur im
/// Protokoll: Ein fehlendes Journal kostet höchstens eine Prüfung mehr, und das darf den
/// Programmstart nicht stören.
/// </summary>
public sealed partial class JsonFileUpdateCheckJournal : IUpdateCheckJournal
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<JsonFileUpdateCheckJournal> _logger;

    /// <summary>Erzeugt das Journal für einen konkreten Dateipfad.</summary>
    /// <param name="filePath">Vollständiger Pfad der Journal-Datei.</param>
    /// <param name="fileSystem">Dateisystem für Lesen und atomares Schreiben.</param>
    /// <param name="logger">Logger für nicht-fatale Lese-/Schreibfehler.</param>
    public JsonFileUpdateCheckJournal(string filePath, IFileSystem fileSystem, ILogger<JsonFileUpdateCheckJournal> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        _filePath = filePath;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> ReadLastCheckAsync(CancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(_filePath))
        {
            return null;
        }

        try
        {
            byte[] content = await _fileSystem.ReadAllBytesAsync(_filePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<JournalState>(content, SerializerOptions)?.LastCheckUtc;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LogReadFailed(_logger, _filePath, ex);
            return null;
        }
    }

    /// <inheritdoc />
    /// <remarks>Ein Abbruch durch den Aufrufer geht als Ausnahme weiter; die alte Datei bleibt dann stehen.</remarks>
    public async Task WriteLastCheckAsync(DateTimeOffset timestampUtc, CancellationToken cancellationToken)
    {
        try
        {
            byte[] content = JsonSerializer.SerializeToUtf8Bytes(new JournalState(timestampUtc), SerializerOptions);
            await _fileSystem.WriteAllBytesAtomicAsync(_filePath, content, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogWriteFailed(_logger, _filePath, ex);
        }
    }

    /// <summary>Inhalt der Journal-Datei: ein JSON-Objekt mit dem Zeitpunkt der letzten erfolgreichen Prüfung.</summary>
    private sealed record JournalState(DateTimeOffset LastCheckUtc);

    [LoggerMessage(EventId = 700, Level = LogLevel.Warning, Message = "Update-Journal nicht lesbar — Prüfung läuft trotzdem: {Path}")]
    private static partial void LogReadFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 701, Level = LogLevel.Warning, Message = "Update-Journal nicht schreibbar — Throttle ohne Persistenz: {Path}")]
    private static partial void LogWriteFailed(ILogger logger, string path, Exception exception);
}
