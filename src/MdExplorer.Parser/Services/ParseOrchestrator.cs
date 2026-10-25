using System.Data.Common;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Hosting;
using MdExplorer.Core.Models;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MdExplorer.Parser.Services;

/// <summary>
/// Treibt den Parse-Lebenszyklus: pollt periodisch nach Markdown-Dateien, deren <c>ContentHash</c>
/// vom gespeicherten <c>SourceContentHash</c> abweicht oder die noch kein Dokument haben.
/// Lesen und Parsen übernimmt <see cref="SourceFileParser"/>, das Schreiben eines Stapels
/// <see cref="ParsedBatchWriter"/>, jeder Stapel in einem eigenen DI-Scope. Dateien mit einem
/// gültigen Fehlschlag-Vermerk bleiben außen vor, bis sich ihr Inhalt oder die Parser-Fassung ändert.
/// </summary>
public sealed partial class ParseOrchestrator : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMarkdownParser _parser;
    private readonly IParseFailureStatus _failureStatus;
    private readonly ParserOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IBackgroundServiceStatus _serviceStatus;
    private readonly ILogger<ParseOrchestrator> _logger;
    private readonly SourceFileParser _sourceParser;
    private readonly ParsedBatchWriter _writer;

    private bool _failureCountReported;

    // Ein Poll-Fehler mit vollem Stapel kommt einmal ins Protokoll; solange er anhält (etwa eine
    // dauerhaft gesperrte Datenbank), folgt je Takt nur eine Zeile auf Debug-Ebene.
    private bool _pollFailureReported;

    /// <summary>Erzeugt den Orchestrator und löst alle Abhängigkeiten auf.</summary>
    public ParseOrchestrator(
        IServiceScopeFactory scopeFactory,
        IFileSystem fileSystem,
        IMarkdownParser parser,
        IParseFailureStatus failureStatus,
        IOptions<ParserOptions> options,
        TimeProvider timeProvider,
        IBackgroundServiceStatus serviceStatus,
        ILogger<ParseOrchestrator> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(failureStatus);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _parser = parser;
        _failureStatus = failureStatus;
        _options = options.Value;
        _timeProvider = timeProvider;
        ArgumentNullException.ThrowIfNull(serviceStatus);
        _serviceStatus = serviceStatus;
        _logger = logger;
        _sourceParser = new SourceFileParser(fileSystem, parser, _options, logger);
        _writer = new ParsedBatchWriter(timeProvider, logger);
    }

    /// <summary>Wrapper um <see cref="RunOnceAsync"/>, der erwartbare Fehler protokolliert statt durchzureichen. Sichtbar für Tests via <c>InternalsVisibleTo</c>.</summary>
    internal async Task TryRunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            _pollFailureReported = false;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is DbException or ArgumentException or InvalidOperationException)
        {
            // Eine Datenbank-Spitze nach dem Wiederholungsbudget oder ein Fehler beim Schreiben
            // eines Stapels beendet nur diesen Durchlauf; der nächste Takt versucht es erneut.
            ReportPollFailure(ex);
        }
    }

    /// <summary>Führt einen kompletten Poll-Durchlauf aus. Sichtbar für Tests via <c>InternalsVisibleTo</c>.</summary>
    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        long startedAt = _timeProvider.GetTimestamp();
        (BatchOutcome total, HashSet<Guid> seenIds) = await ProcessAllBatchesAsync(cancellationToken).ConfigureAwait(false);
        _sourceParser.ForgetFilesOtherThan(seenIds);

        // Bei jedem Durchlauf, auch wenn nichts geschrieben wurde: Ein Schlagwort
        // verliert seine letzte Datei meist gar nicht durch den Parser, sondern weil der
        // Indexer die Datei entfernt hat. Räumte nur ein Durchlauf mit Arbeit auf, blieben nach
        // dem Wegräumen vieler Einträge Schlagworte ohne Datei stehen: Der Parser hätte nichts
        // zu tun und räumte deshalb auch nicht auf. Die Abfrage ist ein Anti-Join über eine kleine
        // Tabelle und kostet wenig.
        await RemoveOrphanedTagsAsync(cancellationToken).ConfigureAwait(false);
        await ReportUnparsableCountAsync(total.FailuresChanged, cancellationToken).ConfigureAwait(false);

        double elapsedMilliseconds = _timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;
        LogPollCompleted(_logger, total.Processed, total.Skipped, elapsedMilliseconds);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(_options.PollIntervalSeconds);
        LogOrchestratorStarted(_logger, interval.TotalSeconds);

        try
        {
            await RunPollLoopAsync(interval, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // erwartete Abbruchsemantik
        }
        catch (Exception ex) when (BackgroundServiceWatchdog.IsRecoverable(ex, stoppingToken))
        {
            // Eine unerwartete Ausnahme aus TryRunOnceAsync beendet den Dienst geordnet und steht im
            // Protokoll; Speichermangel und Stapelüberlauf lässt IsRecoverable durch.
            LogWatchdogTriggered(_logger, ex);
            _serviceStatus.ReportStopped(BackgroundServiceKind.Parser);
        }
        finally
        {
            LogOrchestratorStopped(_logger);
        }
    }

    [LoggerMessage(EventId = 200, Level = LogLevel.Information, Message = "Parser-Orchestrator gestartet — Polling alle {IntervalSeconds:F0} s.")]
    private static partial void LogOrchestratorStarted(ILogger logger, double intervalSeconds);

    [LoggerMessage(EventId = 201, Level = LogLevel.Information, Message = "Parser-Orchestrator gestoppt.")]
    private static partial void LogOrchestratorStopped(ILogger logger);

    [LoggerMessage(EventId = 202, Level = LogLevel.Debug, Message = "Poll-Durchlauf abgeschlossen — {Processed} verarbeitet, {Skipped} übersprungen in {ElapsedMs:F0} ms.")]
    private static partial void LogPollCompleted(ILogger logger, int processed, int skipped, double elapsedMs);

    [LoggerMessage(EventId = 207, Level = LogLevel.Warning, Message = "Parser-Poll übersprungen — nächster Takt versucht es erneut.")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 208, Level = LogLevel.Error, Message = "ParseOrchestrator-Watchdog: unerwartete Exception aufgefangen, Service wird ordentlich beendet.")]
    private static partial void LogWatchdogTriggered(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 209, Level = LogLevel.Information, Message = "{Count} Schlagwort/Schlagworte ohne Datei entfernt.")]
    private static partial void LogOrphanedTagsRemoved(ILogger logger, int count);

    [LoggerMessage(EventId = 213, Level = LogLevel.Debug, Message = "Parser-Poll weiterhin übersprungen: {Reason}")]
    private static partial void LogPollFailedAgain(ILogger logger, string reason);

    // Der Zeitgeber entsteht vor dem ersten Lauf: Der Takt hängt dann nicht davon ab, wie lange der
    // erste Lauf über einen großen Bestand dauert.
    private async Task RunPollLoopAsync(TimeSpan interval, CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(interval, _timeProvider);
        await TryRunOnceAsync(stoppingToken).ConfigureAwait(false);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await TryRunOnceAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    // Zwei Bereiche, mit Absicht: Der Lesebereich hält die laufende Aufzählung offen, jeder
    // Stapel schreibt in einem eigenen. Liefe der ganze Durchlauf in einem einzigen
    // Bereich, sammelte der Änderungsverfolger jedes Dokument und jedes Schlagwort des
    // gesamten Bestands, und SaveChangesAsync durchliefe bei jedem Stapel alles bereits
    // Verfolgte. Über 29.889 Dateien heißt das: 9 GB Arbeitsspeicher und ein Stapel, der von
    // einer Sekunde auf zweieinhalb Minuten anwächst. Der Indexer macht es aus demselben Grund so.
    private async Task<(BatchOutcome Total, HashSet<Guid> SeenIds)> ProcessAllBatchesAsync(CancellationToken cancellationToken)
    {
        BatchOutcome total = default;
        HashSet<Guid> seenIds = [];
        AsyncServiceScope readScope = _scopeFactory.CreateAsyncScope();
        await using (readScope.ConfigureAwait(false))
        {
            IMarkdownSourceProvider sourceProvider = readScope.ServiceProvider.GetRequiredService<IMarkdownSourceProvider>();
            List<MarkdownSourceSnapshot> batch = new(_options.BatchSize);
            await foreach (MarkdownSourceSnapshot snapshot in sourceProvider.EnumerateAsync(cancellationToken).ConfigureAwait(false))
            {
                _ = seenIds.Add(snapshot.Id);
                batch.Add(snapshot);
                if (batch.Count >= _options.BatchSize)
                {
                    total = total.Add(await ProcessBatchInOwnScopeAsync(batch, cancellationToken).ConfigureAwait(false));
                    batch.Clear();
                }
            }
            if (batch.Count > 0)
            {
                total = total.Add(await ProcessBatchInOwnScopeAsync(batch, cancellationToken).ConfigureAwait(false));
            }
        }
        return (total, seenIds);
    }

    /// <summary>Schreibt einen Stapel in einem eigenen Bereich, der Verfolger stirbt mit ihm.</summary>
    private async Task<BatchOutcome> ProcessBatchInOwnScopeAsync(List<MarkdownSourceSnapshot> batch, CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            IServiceProvider services = scope.ServiceProvider;
            return await ProcessBatchAsync(
                services.GetRequiredService<IMarkdownDocumentRepository>(),
                services.GetRequiredService<ITagRepository>(),
                services.GetRequiredService<IParseFailureRepository>(),
                batch,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<BatchOutcome> ProcessBatchAsync(
        IMarkdownDocumentRepository docRepo,
        ITagRepository tagRepo,
        IParseFailureRepository failureRepo,
        List<MarkdownSourceSnapshot> batch,
        CancellationToken cancellationToken)
    {
        string engineVersion = _parser.EngineVersion;
        ParseTargets? selected = await ParseTargets.SelectAsync(docRepo, failureRepo, batch, engineVersion, cancellationToken).ConfigureAwait(false);
        if (selected is not { } chosen)
        {
            return new BatchOutcome(0, batch.Count, FailuresChanged: false);
        }

        (List<MarkdownSourceSnapshot> targets, IReadOnlyDictionary<Guid, ParseFailure> knownFailures) = chosen;
        ParseCollector collected = await _sourceParser.ParseAllAsync(targets, cancellationToken).ConfigureAwait(false);
        await _writer.PersistParsedAsync(docRepo, tagRepo, collected.Parsed, cancellationToken).ConfigureAwait(false);
        bool failuresChanged = await _writer.SyncFailureMarksAsync(
            failureRepo, collected, knownFailures, engineVersion, cancellationToken).ConfigureAwait(false);

        _ = await docRepo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (failuresChanged)
        {
            _ = await failureRepo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        return new BatchOutcome(collected.Parsed.Count, batch.Count - collected.Parsed.Count, failuresChanged);
    }

    /// <summary>Räumt Schlagworte weg, an denen nach diesem Durchlauf keine Datei mehr hängt.</summary>
    private async Task RemoveOrphanedTagsAsync(CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            ITagRepository tagRepo = scope.ServiceProvider.GetRequiredService<ITagRepository>();
            int removed = await tagRepo.RemoveOrphanedTagsAsync(cancellationToken).ConfigureAwait(false);
            if (removed > 0)
            {
                LogOrphanedTagsRemoved(_logger, removed);
            }
        }
    }

    /// <summary>
    /// Meldet die Zahl der nicht verarbeitbaren Dateien an die Betriebsanzeige. Gezählt wird nur
    /// beim ersten Durchlauf und danach, wenn dieser Durchlauf einen Vermerk angelegt oder
    /// aufgehoben hat, sonst wäre es alle paar Sekunden dieselbe Abfrage mit demselben Ergebnis.
    /// </summary>
    private async Task ReportUnparsableCountAsync(bool failuresChanged, CancellationToken cancellationToken)
    {
        if (_failureCountReported && !failuresChanged)
        {
            return;
        }

        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            IParseFailureRepository failureRepo = scope.ServiceProvider.GetRequiredService<IParseFailureRepository>();
            int count = await failureRepo.CountAsync(cancellationToken).ConfigureAwait(false);
            _failureCountReported = true;
            _failureStatus.Update(count);
        }
    }

    private void ReportPollFailure(Exception exception)
    {
        if (_pollFailureReported)
        {
            LogPollFailedAgain(_logger, exception.Message);
            return;
        }
        _pollFailureReported = true;
        LogPollFailed(_logger, exception);
    }
}
