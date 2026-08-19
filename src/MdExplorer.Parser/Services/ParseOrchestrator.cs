using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Hosting;
using MdExplorer.Core.Models;
using MdExplorer.Core.Text;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.Models;
using MdExplorer.Parser.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MdExplorer.Parser.Services;

/// <summary>
/// Treibt den Parse-Lebenszyklus: pollt periodisch nach Markdown-Dateien, deren <c>ContentHash</c>
/// vom gespeicherten <c>SourceContentHash</c> abweicht oder die noch kein Dokument haben.
/// Parsing läuft parallel (in-memory), Persistenz sequentiell innerhalb eines DI-Scopes.
/// Dateien mit einem gültigen Fehlschlag-Vermerk bleiben außen vor, bis sich ihr Inhalt
/// oder die Parser-Fassung ändert.
/// </summary>
public sealed partial class ParseOrchestrator : BackgroundService
{
    /// <summary>Obergrenze für den gespeicherten Fehlschlag-Grund — passend zur Spaltenlänge.</summary>
    private const int FailureReasonMaxLength = 512;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileSystem _fileSystem;
    private readonly IMarkdownParser _parser;
    private readonly IParseFailureStatus _failureStatus;
    private readonly ParserOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ParseOrchestrator> _logger;

    private bool _failureCountReported;

    /// <summary>Erzeugt den Orchestrator und löst alle Abhängigkeiten auf.</summary>
    public ParseOrchestrator(
        IServiceScopeFactory scopeFactory,
        IFileSystem fileSystem,
        IMarkdownParser parser,
        IParseFailureStatus failureStatus,
        IOptions<ParserOptions> options,
        TimeProvider timeProvider,
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
        _fileSystem = fileSystem;
        _parser = parser;
        _failureStatus = failureStatus;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Wrapper um <see cref="RunOnceAsync"/>, der erwartbare Fehler frisst. Sichtbar für Tests via <c>InternalsVisibleTo</c>.</summary>
    internal async Task TryRunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbException ex)
        {
            // SQLite-Spitze nach Retry-Budget darf den Parser-Lebenszyklus nicht killen.
            LogPollFailed(_logger, ex);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Defense-in-Depth. Falls eine Markdig-/Yaml-/JSON-Exception über den
            // ParseOneAsync-Catch hinausschlüpft (z. B. aus einem Sub-Renderer oder einem
            // Frontmatter-Pfad), bleibt der Periodic-Tick-Loop am Leben — nächster Tick
            // versucht es erneut, ohne dass ein einziges kaputtes File den Service killt.
            LogPollFailed(_logger, ex);
        }
    }

    /// <summary>Führt einen kompletten Poll-Durchlauf aus. Sichtbar für Tests via <c>InternalsVisibleTo</c>.</summary>
    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        long startedAt = _timeProvider.GetTimestamp();
        BatchOutcome total = default;

        // Zwei Bereiche, mit Absicht: Der Lesebereich hält die laufende Aufzählung offen, jeder
        // Stapel schreibt in einem eigenen. Vorher lief der ganze Durchlauf in einem einzigen
        // Bereich — der Änderungsverfolger sammelte dann jedes Dokument und jedes Schlagwort
        // des gesamten Bestands, und `SaveChangesAsync` durchlief bei jedem Stapel alles bereits
        // Verfolgte. Über 29.889 Dateien hieß das am 16.08.2026: 9 GB Arbeitsspeicher und ein
        // Stapel, der von einer Sekunde auf zweieinhalb Minuten anwuchs. Der Indexer macht es
        // aus demselben Grund seit jeher so.
        AsyncServiceScope readScope = _scopeFactory.CreateAsyncScope();
        await using (readScope.ConfigureAwait(false))
        {
            IMarkdownSourceProvider sourceProvider = readScope.ServiceProvider.GetRequiredService<IMarkdownSourceProvider>();

            List<MarkdownSourceSnapshot> batch = new(_options.BatchSize);
            await foreach (MarkdownSourceSnapshot snapshot in sourceProvider.EnumerateAsync(cancellationToken).ConfigureAwait(false))
            {
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

        // Bewusst bei jedem Durchlauf, nicht nur wenn etwas geschrieben wurde: Ein Schlagwort
        // verliert seine letzte Datei meist gar nicht durch den Parser, sondern weil der
        // Indexer die Datei entfernt hat. Am 16.08.2026 blieben nach dem Wegräumen von 14.081
        // Einträgen 249 Schlagworte ohne Datei stehen — der Parser hatte nichts zu tun und
        // räumte deshalb auch nicht auf. Die Abfrage ist ein Anti-Join über eine kleine
        // Tabelle; sie kostet nichts.
        await RemoveOrphanedTagsAsync(cancellationToken).ConfigureAwait(false);
        await ReportUnparsableCountAsync(total.FailuresChanged, cancellationToken).ConfigureAwait(false);

        TimeSpan elapsed = _timeProvider.GetElapsedTime(startedAt);
        LogPollCompleted(_logger, total.Processed, total.Skipped, elapsed.TotalMilliseconds);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(_options.PollIntervalSeconds);
        LogOrchestratorStarted(_logger, interval.TotalSeconds);

        try
        {
            await TryRunOnceAsync(stoppingToken).ConfigureAwait(false);

            using PeriodicTimer timer = new(interval, _timeProvider);
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    return;
                }
                await TryRunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // erwartete Abbruchsemantik
        }
        catch (Exception ex) when (BackgroundServiceWatchdog.IsRecoverable(ex))
        {
            // Letzte Schicht. Wenn TryRunOnceAsync trotz der Defense-in-Depth-Catches eine
            // unerwartete Exception durchreicht, beendet der Watchdog den Service ordentlich
            // (geloggt) statt unhandled. OOM/StackOverflow werden weiter durchgereicht.
            LogWatchdogTriggered(_logger, ex);
        }
        finally
        {
            LogOrchestratorStopped(_logger);
        }
    }

    // Sammelt alle einzigartigen Tag-Slugs aus dem Batch, fragt vorhandene in einem
    // einzigen Roundtrip ab und legt fehlende genau einmal an. Liefert einen vollständigen
    // Slug→Id-Lookup, über den die per-File-Verlinkung deterministisch arbeitet.
    private static async Task<Dictionary<string, Guid>> EnsureTagsForBatchAsync(
        ITagRepository tagRepo,
        List<ParsedEntry> results,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> namesBySlug = new(StringComparer.Ordinal);
        foreach (ParseResult result in results.Select(entry => entry.Result))
        {
            IReadOnlyList<string> slugs = result.Tags;
            IReadOnlyList<string> names = result.TagNames;
            for (int i = 0; i < slugs.Count; i++)
            {
                _ = namesBySlug.TryAdd(slugs[i], names[i]);
            }
        }
        if (namesBySlug.Count == 0)
        {
            return new Dictionary<string, Guid>(StringComparer.Ordinal);
        }

        IReadOnlyList<Tag> existing = await tagRepo.GetBySlugsAsync(namesBySlug.Keys, cancellationToken).ConfigureAwait(false);
        Dictionary<string, Guid> slugToId = existing.ToDictionary(tag => tag.Slug, tag => tag.Id, StringComparer.Ordinal);

        foreach (KeyValuePair<string, string> pair in namesBySlug)
        {
            if (slugToId.ContainsKey(pair.Key))
            {
                continue;
            }
            Tag created = new()
            {
                Id = Guid.NewGuid(),
                Name = pair.Value,
                Slug = pair.Key,
            };
            await tagRepo.AddAsync(created, cancellationToken).ConfigureAwait(false);
            slugToId[pair.Key] = created.Id;
        }
        return slugToId;
    }

    private static async Task SyncFileTagLinksAsync(
        ITagRepository tagRepo,
        ParsedEntry entry,
        Dictionary<string, Guid> slugToId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> slugs = entry.Result.Tags;
        if (slugs.Count == 0)
        {
            await tagRepo.ReplaceFileTagsAsync(entry.Snapshot.Id, [], cancellationToken).ConfigureAwait(false);
            return;
        }
        Guid[] tagIds = [.. slugs.Select(slug => slugToId[slug])];
        await tagRepo.ReplaceFileTagsAsync(entry.Snapshot.Id, tagIds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Kürzt Ausnahmetyp und Meldung auf die Spaltenlänge — der volle Stapel steht im Protokoll.</summary>
    private static string BuildFailureReason(Exception exception)
    {
        string reason = string.Create(CultureInfo.InvariantCulture, $"{exception.GetType().Name}: {exception.Message}");
        return reason.Length <= FailureReasonMaxLength ? reason : reason[..FailureReasonMaxLength];
    }

    /// <summary>Ein Vermerk gilt nur für genau den Inhalt und genau die Parser-Fassung, an denen er entstanden ist.</summary>
    private static bool IsStillBinding(ParseFailure failure, MarkdownSourceSnapshot snapshot, string engineVersion) =>
        string.Equals(failure.ContentHash, snapshot.ContentHash, StringComparison.Ordinal)
        && string.Equals(failure.EngineVersion, engineVersion, StringComparison.Ordinal);

    /// <summary>
    /// Ohne diese Prüfung stünde eine unparsbare Datei bei jedem Tick wieder im Stapel: Sie
    /// bekommt kein Dokument, bleibt damit dauerhaft veraltet und wurde bis dahin alle paar
    /// Sekunden neu gelesen, dekodiert und durch den Parser geschickt — für ein Ergebnis, das
    /// längst feststand.
    /// </summary>
    private static bool NeedsParseAttempt(
        MarkdownSourceSnapshot snapshot,
        IReadOnlyDictionary<Guid, ParseFailure> knownFailures,
        string engineVersion) =>
        !knownFailures.TryGetValue(snapshot.Id, out ParseFailure? failure)
        || !IsStillBinding(failure, snapshot, engineVersion);

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
    /// aufgehoben hat — sonst wäre es alle paar Sekunden dieselbe Abfrage mit demselben Ergebnis.
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

    /// <summary>Schreibt einen Stapel in einem eigenen Bereich — der Verfolger stirbt mit ihm.</summary>
    private async Task<BatchOutcome> ProcessBatchInOwnScopeAsync(
        List<MarkdownSourceSnapshot> batch,
        CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            IMarkdownDocumentRepository docRepo = scope.ServiceProvider.GetRequiredService<IMarkdownDocumentRepository>();
            ITagRepository tagRepo = scope.ServiceProvider.GetRequiredService<ITagRepository>();
            IParseFailureRepository failureRepo = scope.ServiceProvider.GetRequiredService<IParseFailureRepository>();

            return await ProcessBatchAsync(docRepo, tagRepo, failureRepo, batch, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<BatchOutcome> ProcessBatchAsync(
        IMarkdownDocumentRepository docRepo,
        ITagRepository tagRepo,
        IParseFailureRepository failureRepo,
        List<MarkdownSourceSnapshot> batch,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, string> hashes = batch.ToDictionary(s => s.Id, s => s.ContentHash);
        IReadOnlyList<Guid> stale = await docRepo.GetStaleOrMissingAsync(hashes, cancellationToken).ConfigureAwait(false);
        if (stale.Count == 0)
        {
            return new BatchOutcome(0, batch.Count, FailuresChanged: false);
        }

        HashSet<Guid> staleSet = [.. stale];
        List<MarkdownSourceSnapshot> candidates = [.. batch.Where(snapshot => staleSet.Contains(snapshot.Id))];

        IReadOnlyDictionary<Guid, ParseFailure> knownFailures = await failureRepo
            .GetByMarkdownFileIdsAsync([.. candidates.Select(snapshot => snapshot.Id)], cancellationToken)
            .ConfigureAwait(false);
        string engineVersion = _parser.EngineVersion;
        List<MarkdownSourceSnapshot> targets = [.. candidates.Where(snapshot => NeedsParseAttempt(snapshot, knownFailures, engineVersion))];

        ParseCollector collected = await ParseInParallelAsync(targets, cancellationToken).ConfigureAwait(false);
        List<ParsedEntry> results = collected.Parsed;

        await PersistParsedAsync(docRepo, tagRepo, results, cancellationToken).ConfigureAwait(false);

        bool failuresChanged = await SyncFailureMarksAsync(
            failureRepo, collected, knownFailures, engineVersion, cancellationToken).ConfigureAwait(false);

        _ = await docRepo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (failuresChanged)
        {
            _ = await failureRepo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new BatchOutcome(results.Count, batch.Count - results.Count, failuresChanged);
    }

    /// <summary>Legt Dokumente und Schlagwort-Verknüpfungen des Stapels an beziehungsweise aktualisiert sie.</summary>
    private async Task PersistParsedAsync(
        IMarkdownDocumentRepository docRepo,
        ITagRepository tagRepo,
        List<ParsedEntry> results,
        CancellationToken cancellationToken)
    {
        // Existierende Dokumente einmal je Batch laden statt pro Datei per Point-Lookup — die
        // Ziel-Ids stehen zum Batch-Start bereits fest. PersistDocumentAsync konsumiert daraus.
        Guid[] resultIds = [.. results.Select(entry => entry.Snapshot.Id)];
        IReadOnlyDictionary<Guid, MarkdownDocument> existingByFileId =
            await docRepo.GetByMarkdownFileIdsAsync(resultIds, cancellationToken).ConfigureAwait(false);

        // Tag-Cache für den ganzen Batch — sonst ruft jede Datei einen frischen
        // GetBySlugsAsync auf und addiert denselben Slug doppelt; SaveChanges scheitert dann mit
        // SqliteException 19 (UNIQUE constraint failed: Tags.Slug). Wir sammeln die Slugs einmal,
        // resolven die existierenden Tags in einem einzigen Roundtrip und legen die fehlenden
        // genau einmal pro Slug an.
        Dictionary<string, Guid> slugToId = await EnsureTagsForBatchAsync(tagRepo, results, cancellationToken).ConfigureAwait(false);

        foreach (ParsedEntry entry in results)
        {
            await PersistDocumentAsync(docRepo, existingByFileId, entry, cancellationToken).ConfigureAwait(false);
            await SyncFileTagLinksAsync(tagRepo, entry, slugToId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Schreibt die Fehlschläge dieses Stapels ins Protokoll und in die Datenbank und hebt die
    /// Vermerke der Dateien auf, die wieder parsbar sind. Liefert, ob sich am Bestand der
    /// Vermerke etwas geändert hat.
    /// </summary>
    private async Task<bool> SyncFailureMarksAsync(
        IParseFailureRepository failureRepo,
        ParseCollector collected,
        IReadOnlyDictionary<Guid, ParseFailure> knownFailures,
        string engineVersion,
        CancellationToken cancellationToken)
    {
        Guid[] recovered = [.. collected.Parsed
            .Select(entry => entry.Snapshot.Id)
            .Where(knownFailures.ContainsKey)];
        await failureRepo.RemoveAsync(recovered, cancellationToken).ConfigureAwait(false);

        foreach (ParseFailureEntry entry in collected.Failed)
        {
            LogFailure(entry, knownFailures);
            await failureRepo.RecordAsync(
                new ParseFailure
                {
                    Id = Guid.NewGuid(),
                    MarkdownFileId = entry.Snapshot.Id,
                    ContentHash = entry.Snapshot.ContentHash,
                    EngineVersion = engineVersion,
                    FailureReason = BuildFailureReason(entry.Failure),
                    FailedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
                },
                cancellationToken).ConfigureAwait(false);
        }

        return recovered.Length > 0 || collected.Failed.Count > 0;
    }

    /// <summary>
    /// Der volle Aufrufstapel geht genau einmal je Datei und Inhalt ins Protokoll. Der Renderer
    /// arbeitet rekursiv — ein einziger Stapel dieser Sorte ist rund 265 Zeilen lang, und
    /// wiederholt geschrieben verdrängt er jede echte Diagnose aus der Datei.
    /// </summary>
    private void LogFailure(ParseFailureEntry entry, IReadOnlyDictionary<Guid, ParseFailure> knownFailures)
    {
        bool sameContentAlreadyLogged =
            knownFailures.TryGetValue(entry.Snapshot.Id, out ParseFailure? previous)
            && string.Equals(previous.ContentHash, entry.Snapshot.ContentHash, StringComparison.Ordinal);

        if (sameContentAlreadyLogged)
        {
            LogParseFailedAgain(_logger, entry.Snapshot.AbsolutePath, BuildFailureReason(entry.Failure));
            return;
        }
        LogParseFailed(_logger, entry.Snapshot.AbsolutePath, entry.Failure);
    }

    private async Task<ParseCollector> ParseInParallelAsync(
        List<MarkdownSourceSnapshot> targets,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim semaphore = new(_options.MaxParallelism);
        try
        {
            ParseCollector collector = new();

            Task[] tasks = targets
                .Select(snapshot => ParseOneAsync(snapshot, semaphore, collector, cancellationToken))
                .ToArray();

            await Task.WhenAll(tasks).ConfigureAwait(false);
            return collector;
        }
        finally
        {
            semaphore.Dispose();
        }
    }

    private async Task ParseOneAsync(
        MarkdownSourceSnapshot snapshot,
        SemaphoreSlim semaphore,
        ParseCollector collector,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            byte[] bytes;
            try
            {
                bytes = await _fileSystem.ReadAllBytesAsync(snapshot.AbsolutePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
            {
                LogReadFailed(_logger, snapshot.AbsolutePath, ex);
                return;
            }

            string markdown = Utf8Decoder.DecodeNoBom(bytes);
            ParseResult parseResult;
            try
            {
                parseResult = _parser.Parse(markdown);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                // Markdig wirft ArgumentException u. a. bei depth-limit-Verstößen
                // (zu tief verschachtelte Emphasis/Listen). InvalidOperationException kommt
                // aus dem Yaml-/Frontmatter-Pfad. Beide sind Format-Fehler im Dokument —
                // Datei überspringen, restlicher Batch läuft weiter. Protokolliert und
                // vermerkt wird der Fehlschlag gesammelt in SyncFailureMarksAsync.
                collector.AddFailure(new ParseFailureEntry(snapshot, ex));
                return;
            }

            collector.AddParsed(new ParsedEntry(snapshot, parseResult));
        }
        finally
        {
            _ = semaphore.Release();
        }
    }

    private async Task PersistDocumentAsync(
        IMarkdownDocumentRepository docRepo,
        IReadOnlyDictionary<Guid, MarkdownDocument> existingByFileId,
        ParsedEntry entry,
        CancellationToken cancellationToken)
    {
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        string frontmatterJson = JsonSerializer.Serialize(entry.Result.Frontmatter, JsonOptions);
        string outlinksJson = JsonSerializer.Serialize(entry.Result.OutlinkSlugs, JsonOptions);

        if (!existingByFileId.TryGetValue(entry.Snapshot.Id, out MarkdownDocument? existing))
        {
            MarkdownDocument created = new()
            {
                Id = Guid.NewGuid(),
                MarkdownFileId = entry.Snapshot.Id,
                SourceContentHash = entry.Snapshot.ContentHash,
                FrontmatterJson = frontmatterJson,
                OutlinksJson = outlinksJson,
                ParsedAtUtc = now,
            };
            created.SetRenderedHtmlGz(entry.Result.RenderedHtmlGz.Span);
            await docRepo.AddAsync(created, cancellationToken).ConfigureAwait(false);
            LogDocumentAdded(_logger, entry.Snapshot.AbsolutePath);
        }
        else
        {
            existing.SourceContentHash = entry.Snapshot.ContentHash;
            existing.FrontmatterJson = frontmatterJson;
            existing.OutlinksJson = outlinksJson;
            existing.ParsedAtUtc = now;
            existing.SetRenderedHtmlGz(entry.Result.RenderedHtmlGz.Span);
            docRepo.Update(existing);
            LogDocumentUpdated(_logger, entry.Snapshot.AbsolutePath);
        }
    }

    private readonly record struct ParsedEntry(MarkdownSourceSnapshot Snapshot, ParseResult Result);

    private readonly record struct ParseFailureEntry(MarkdownSourceSnapshot Snapshot, Exception Failure);

    /// <summary>Ergebniszahlen eines Stapels — addierbar, damit der Durchlauf sie aufsummieren kann.</summary>
    private readonly record struct BatchOutcome(int Processed, int Skipped, bool FailuresChanged)
    {
        public BatchOutcome Add(BatchOutcome other) =>
            new(Processed + other.Processed, Skipped + other.Skipped, FailuresChanged || other.FailuresChanged);
    }

    /// <summary>Sammelt die Ergebnisse der parallel laufenden Parse-Vorgänge unter einer Sperre.</summary>
    private sealed class ParseCollector
    {
        private readonly Lock _gate = new();

        public List<ParsedEntry> Parsed { get; } = [];

        public List<ParseFailureEntry> Failed { get; } = [];

        public void AddParsed(ParsedEntry entry)
        {
            lock (_gate)
            {
                Parsed.Add(entry);
            }
        }

        public void AddFailure(ParseFailureEntry entry)
        {
            lock (_gate)
            {
                Failed.Add(entry);
            }
        }
    }

    [LoggerMessage(EventId = 200, Level = LogLevel.Information, Message = "Parser-Orchestrator gestartet — Polling alle {IntervalSeconds:F0} s.")]
    private static partial void LogOrchestratorStarted(ILogger logger, double intervalSeconds);

    [LoggerMessage(EventId = 201, Level = LogLevel.Information, Message = "Parser-Orchestrator gestoppt.")]
    private static partial void LogOrchestratorStopped(ILogger logger);

    [LoggerMessage(EventId = 202, Level = LogLevel.Debug, Message = "Poll-Durchlauf abgeschlossen — {Processed} verarbeitet, {Skipped} übersprungen in {ElapsedMs:F0} ms.")]
    private static partial void LogPollCompleted(ILogger logger, int processed, int skipped, double elapsedMs);

    [LoggerMessage(EventId = 203, Level = LogLevel.Debug, Message = "Dokument hinzugefügt: {Path}")]
    private static partial void LogDocumentAdded(ILogger logger, string path);

    [LoggerMessage(EventId = 204, Level = LogLevel.Debug, Message = "Dokument aktualisiert: {Path}")]
    private static partial void LogDocumentUpdated(ILogger logger, string path);

    [LoggerMessage(EventId = 205, Level = LogLevel.Warning, Message = "Datei konnte nicht gelesen werden: {Path}")]
    private static partial void LogReadFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 206, Level = LogLevel.Warning, Message = "Datei konnte nicht geparst werden: {Path}")]
    private static partial void LogParseFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 207, Level = LogLevel.Warning, Message = "Parser-Poll übersprungen — Datenbank-Spitze, nächster Periodic-Tick versucht es erneut.")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 208, Level = LogLevel.Error, Message = "ParseOrchestrator-Watchdog: unerwartete Exception aufgefangen, Service wird ordentlich beendet.")]
    private static partial void LogWatchdogTriggered(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 209, Level = LogLevel.Information, Message = "{Count} Schlagwort/Schlagworte ohne Datei entfernt.")]
    private static partial void LogOrphanedTagsRemoved(ILogger logger, int count);

    [LoggerMessage(EventId = 210, Level = LogLevel.Warning, Message = "Datei weiterhin nicht parsbar, Inhalt unverändert: {Path} — {Reason}")]
    private static partial void LogParseFailedAgain(ILogger logger, string path, string reason);
}
