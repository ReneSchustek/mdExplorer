using System.Collections.Concurrent;
using System.Globalization;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Hosting;
using MdExplorer.Core.Text;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.Models;
using MdExplorer.Parser.Options;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Parser.Services;

/// <summary>
/// Liest und parst die Dateien eines Stapels, parallel bis zur eingestellten Grenze.
/// </summary>
/// <remarks>
/// Jede Datei ist für sich abgeschottet: Was beim Lesen oder Parsen einer Datei schiefgeht,
/// landet als Ergebnis im <see cref="ParseCollector"/>, und der Rest des Stapels läuft weiter.
/// Nur was der Prozess nicht überstehen kann (Speichermangel, Abbruch), geht nach oben.
/// </remarks>
internal sealed partial class SourceFileParser
{
    /// <summary>Obergrenze für den gespeicherten Fehlschlag-Grund, passend zur Spaltenlänge.</summary>
    internal const int FailureReasonMaxLength = 512;

    private readonly IFileSystem _fileSystem;
    private readonly IMarkdownParser _parser;
    private readonly ParserOptions _options;
    private readonly ILogger _logger;

    // Dateien, deren Lesefehler schon mit vollem Stapel im Protokoll steht, mit dem Inhaltsstand
    // zu diesem Zeitpunkt. Nur im Speicher: Nach einem Neustart steht der Stapel einmal neu im
    // Protokoll, und das ist gewollt. Gelesen und geschrieben wird aus den parallelen Lesevorgängen.
    private readonly ConcurrentDictionary<Guid, string> _reportedReadFailures = new();

    /// <summary>Erzeugt den Leser mit Dateisystem, Parser, Grenzen und Protokoll des Orchestrators.</summary>
    public SourceFileParser(IFileSystem fileSystem, IMarkdownParser parser, ParserOptions options, ILogger logger)
    {
        _fileSystem = fileSystem;
        _parser = parser;
        _options = options;
        _logger = logger;
    }

    /// <summary>Kürzt Ausnahmetyp und Meldung auf die Spaltenlänge; der volle Stapel steht im Protokoll.</summary>
    /// <param name="exception">Die Ausnahme.</param>
    /// <returns>Der Grund in einer Zeile.</returns>
    public static string ReasonOf(Exception exception) =>
        Truncate(string.Create(CultureInfo.InvariantCulture, $"{exception.GetType().Name}: {exception.Message}"));

    /// <summary>Liest und parst alle Dateien.</summary>
    /// <param name="targets">Die Dateien, die einen Versuch brauchen.</param>
    /// <param name="cancellationToken">Abbruch.</param>
    /// <returns>Ergebnisse und Fehlschläge.</returns>
    public async Task<ParseCollector> ParseAllAsync(IReadOnlyList<MarkdownSourceSnapshot> targets, CancellationToken cancellationToken)
    {
        using SemaphoreSlim semaphore = new(_options.MaxParallelism);
        ParseCollector collector = new();
        await Task.WhenAll(targets.Select(snapshot => ParseOneAsync(snapshot, semaphore, collector, cancellationToken))).ConfigureAwait(false);
        return collector;
    }

    /// <summary>Vergisst gemeldete Lesefehler von Dateien, die nicht mehr im Index stehen.</summary>
    /// <remarks>Sonst wüchse die Liste über die Laufzeit mit jeder gelöschten Datei, deren Lesen einmal scheiterte.</remarks>
    /// <param name="currentIds">Die Dateien des letzten Durchlaufs.</param>
    public void ForgetFilesOtherThan(IReadOnlySet<Guid> currentIds)
    {
        foreach (Guid id in _reportedReadFailures.Keys.Where(id => !currentIds.Contains(id)))
        {
            _ = _reportedReadFailures.TryRemove(id, out _);
        }
    }

    private static string Truncate(string reason) =>
        reason.Length <= FailureReasonMaxLength ? reason : reason[..FailureReasonMaxLength];

    [LoggerMessage(EventId = 205, Level = LogLevel.Warning, Message = "Datei konnte nicht gelesen werden: {Path}")]
    private static partial void LogReadFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 211, Level = LogLevel.Debug, Message = "Datei weiterhin nicht lesbar: {Path} — {Reason}")]
    private static partial void LogReadFailedAgain(ILogger logger, string path, string reason);

    private async Task ParseOneAsync(
        MarkdownSourceSnapshot snapshot,
        SemaphoreSlim semaphore,
        ParseCollector collector,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ReadOutcome read = await TryReadAsync(snapshot, collector, cancellationToken).ConfigureAwait(false);
            if (read.Succeeded)
            {
                Parse(snapshot, Utf8Decoder.DecodeNoBom(read.Bytes), collector);
            }
        }
        finally
        {
            _ = semaphore.Release();
        }
    }

    /// <summary>
    /// Liest die Datei, wenn sie die Größengrenze einhält. Eine zu große Datei wird nicht gelesen,
    /// sondern als Fehlschlag vermerkt: Sie bleibt dann außen vor, bis sich ihr Inhalt ändert.
    /// </summary>
    private async Task<ReadOutcome> TryReadAsync(MarkdownSourceSnapshot snapshot, ParseCollector collector, CancellationToken cancellationToken)
    {
        try
        {
            long size = _fileSystem.GetFileSize(snapshot.AbsolutePath);
            if (size > _options.MaxFileSizeBytes)
            {
                string reason = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Datei mit {size} Byte größer als die Grenze von {_options.MaxFileSizeBytes} Byte");
                collector.AddFailure(new ParseFailureEntry(snapshot, Truncate(reason), Failure: null));
                return ReadOutcome.Nothing;
            }
            byte[] bytes = await _fileSystem.ReadAllBytesAsync(snapshot.AbsolutePath, cancellationToken).ConfigureAwait(false);
            _ = _reportedReadFailures.TryRemove(snapshot.Id, out _);
            return new ReadOutcome(bytes, Succeeded: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportReadFailure(snapshot, ex);
            return ReadOutcome.Nothing;
        }
    }

    // Was der Parser an einer einzelnen Datei nicht verkraftet, kostet nur diese Datei: Markdig
    // wirft ArgumentException bei zu tiefer Verschachtelung, eine Zeitgrenze im Hashtag-Ausdruck
    // RegexMatchTimeoutException, und für Eingaben, die niemand vorhersieht, gilt dasselbe.
    // Protokolliert und vermerkt wird gesammelt beim Schreiben des Stapels.
    private void Parse(MarkdownSourceSnapshot snapshot, string markdown, ParseCollector collector)
    {
        ParseResult result;
        try
        {
            result = _parser.Parse(markdown);
        }
        catch (Exception ex) when (BackgroundServiceWatchdog.IsRecoverable(ex, CancellationToken.None))
        {
            collector.AddFailure(new ParseFailureEntry(snapshot, ReasonOf(ex), ex));
            return;
        }
        collector.AddParsed(new ParsedEntry(snapshot, result));
    }

    /// <summary>
    /// Protokolliert einen Lesefehler, mit vollem Stapel nur beim ersten Mal je Datei und Inhalt.
    /// </summary>
    /// <remarks>
    /// Eine gesperrte Datei bleibt veraltet und steht deshalb bei jedem Takt wieder im Stapel. Der
    /// neue Versuch ist richtig, denn die Sperre kann jederzeit enden. Wiederholt geschrieben
    /// verdrängte der Stapel aber alle paar Sekunden jede andere Meldung aus dem Protokoll; die
    /// Wiederholungen stehen deshalb als eine Zeile auf Debug-Ebene. Ein neuer Inhalt oder ein
    /// Fehler nach einem erfolgreichen Lesen ist ein neues Ereignis und kommt wieder vollständig.
    /// </remarks>
    private void ReportReadFailure(MarkdownSourceSnapshot snapshot, Exception exception)
    {
        bool alreadyReported =
            _reportedReadFailures.TryGetValue(snapshot.Id, out string? reportedHash)
            && string.Equals(reportedHash, snapshot.ContentHash, StringComparison.Ordinal);
        if (alreadyReported)
        {
            LogReadFailedAgain(_logger, snapshot.AbsolutePath, exception.Message);
            return;
        }
        _reportedReadFailures[snapshot.Id] = snapshot.ContentHash;
        LogReadFailed(_logger, snapshot.AbsolutePath, exception);
    }

    /// <summary>Ergebnis des Lesens: die Bytes, oder nichts, wenn die Datei übersprungen wurde.</summary>
    /// <param name="Bytes">Der Inhalt der Datei.</param>
    /// <param name="Succeeded">Ob gelesen wurde; eine leere Datei ist ein gültiger Inhalt.</param>
    private readonly record struct ReadOutcome(byte[] Bytes, bool Succeeded)
    {
        public static ReadOutcome Nothing => new([], Succeeded: false);
    }
}
