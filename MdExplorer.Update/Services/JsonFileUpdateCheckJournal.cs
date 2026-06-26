using System.Text.Json;
using System.Text.Json.Serialization;
using MdExplorer.Update.Abstractions;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Update.Services;

/// <summary>
/// Datei-basierte Implementierung von <see cref="IUpdateCheckJournal"/>. Persistiert den
/// Zeitstempel der letzten Prüfung als kleine JSON-Datei und schreibt atomar (<c>.tmp</c> +
/// <see cref="File.Move(string, string, bool)"/>). Lese- und Schreibfehler werden bewusst
/// verschluckt — ein fehlendes Journal darf den Programmstart niemals stören.
/// </summary>
public sealed partial class JsonFileUpdateCheckJournal : IUpdateCheckJournal
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private readonly ILogger<JsonFileUpdateCheckJournal> _logger;

    /// <summary>Erzeugt das Journal für einen konkreten Dateipfad.</summary>
    /// <param name="filePath">Vollständiger Pfad der Journal-Datei.</param>
    /// <param name="logger">Logger für nicht-fatale Lese-/Schreibfehler.</param>
    public JsonFileUpdateCheckJournal(string filePath, ILogger<JsonFileUpdateCheckJournal> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(logger);
        _filePath = filePath;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> ReadLastCheckAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            FileStream stream = File.Open(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            JournalState? state;
            await using (stream.ConfigureAwait(false))
            {
                state = await JsonSerializer
                    .DeserializeAsync<JournalState>(stream, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            return state?.LastCheckUtc;
        }
        catch (JsonException ex)
        {
            LogReadFailed(_logger, _filePath, ex);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogReadFailed(_logger, _filePath, ex);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task WriteLastCheckAsync(DateTimeOffset timestampUtc, CancellationToken cancellationToken)
    {
        try
        {
            string directory = Path.GetDirectoryName(_filePath)
                ?? throw new InvalidOperationException("Journal-Pfad hat kein Verzeichnis.");
            _ = Directory.CreateDirectory(directory);

            string tempPath = _filePath + ".tmp";
            FileStream stream = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await using (stream.ConfigureAwait(false))
            {
                await JsonSerializer
                    .SerializeAsync(stream, new JournalState(timestampUtc), SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogWriteFailed(_logger, _filePath, ex);
        }
    }

    private sealed record JournalState(DateTimeOffset LastCheckUtc);

    [LoggerMessage(EventId = 700, Level = LogLevel.Debug, Message = "Update-Journal nicht lesbar — Prüfung läuft trotzdem: {Path}")]
    private static partial void LogReadFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 701, Level = LogLevel.Debug, Message = "Update-Journal nicht schreibbar — Throttle ohne Persistenz: {Path}")]
    private static partial void LogWriteFailed(ILogger logger, string path, Exception exception);
}
