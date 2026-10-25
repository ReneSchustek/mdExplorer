using System.IO;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Hosting;
using MdExplorer.Core.Text;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.Models;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Liest und übersetzt eine Datei, die der Indexer noch nicht kennt, direkt von der Platte.
/// </summary>
internal sealed partial class UnindexedFileReader
{
    private readonly IFileSystem _fileSystem;
    private readonly IMarkdownParser _markdownParser;
    private readonly ILogger _logger;

    /// <summary>Erzeugt den Leser.</summary>
    /// <param name="fileSystem">Woraus gelesen wird.</param>
    /// <param name="markdownParser">Übersetzt den Text für die Vorschau.</param>
    /// <param name="logger">Protokoll des Bereichs, für den gelesen wird.</param>
    public UnindexedFileReader(IFileSystem fileSystem, IMarkdownParser markdownParser, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(markdownParser);
        ArgumentNullException.ThrowIfNull(logger);

        _fileSystem = fileSystem;
        _markdownParser = markdownParser;
        _logger = logger;
    }

    /// <summary>Liest die Datei und übersetzt sie.</summary>
    /// <param name="absolutePath">Pfad der Datei.</param>
    /// <param name="load">Der Ladevorgang; ist er überholt, wird nicht mehr übersetzt.</param>
    /// <returns>
    /// Inhalt und Übersetzung, oder <see langword="null"/>, wenn die Datei fehlt, gesperrt ist,
    /// das Lesen abgebrochen wurde oder Markdig den Text abweist.
    /// </returns>
    public async Task<UnindexedFile?> ReadAsync(string absolutePath, LatestOperation.Ticket load)
    {
        ArgumentNullException.ThrowIfNull(load);
        if (!_fileSystem.FileExists(absolutePath))
        {
            LogMissingFile(_logger, absolutePath);
            return null;
        }
        byte[] bytes;
        try
        {
            bytes = await _fileSystem.ReadAllBytesAsync(absolutePath, load.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogReadFailed(_logger, absolutePath, ex);
            return null;
        }
        ParseResult? parsed = load.IsCurrent ? Parse(absolutePath, bytes) : null;
        return parsed is null ? null : new UnindexedFile(bytes, parsed);
    }

    [LoggerMessage(EventId = 513, Level = LogLevel.Warning, Message = "Direkt-Load für {Path} fehlgeschlagen.")]
    private static partial void LogReadFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 514, Level = LogLevel.Warning, Message = "Direkt-Load übersprungen — Datei {Path} existiert nicht.")]
    private static partial void LogMissingFile(ILogger logger, string path);

    private ParseResult? Parse(string absolutePath, byte[] bytes)
    {
        try
        {
            return _markdownParser.Parse(Utf8Decoder.DecodeNoBom(bytes));
        }
        catch (Exception ex) when (BackgroundServiceWatchdog.IsRecoverable(ex, CancellationToken.None))
        {
            // Welche Ausnahme Markdig für einen abgewiesenen Text wirft, bestimmt die Bibliothek.
            // Ein Klick auf eine solche Datei soll den Bereich leeren, nicht das Programm beenden.
            LogReadFailed(_logger, absolutePath, ex);
            return null;
        }
    }
}

/// <summary>Eine direkt gelesene Datei: die Bytes für den Editor, die Übersetzung für die Vorschau.</summary>
/// <param name="Bytes">Der gelesene Inhalt.</param>
/// <param name="Parsed">Die Übersetzung durch den Parser.</param>
internal sealed record UnindexedFile(byte[] Bytes, ParseResult Parsed);
