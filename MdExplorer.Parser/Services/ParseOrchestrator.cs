using System.Data.Common;
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
/// </summary>
public sealed partial class ParseOrchestrator : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileSystem _fileSystem;
    private readonly IMarkdownParser _parser;
    private readonly ParserOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ParseOrchestrator> _logger;

    /// <summary>Erzeugt den Orchestrator und löst alle Abhängigkeiten auf.</summary>
    public ParseOrchestrator(
        IServiceScopeFactory scopeFactory,
        IFileSystem fileSystem,
        IMarkdownParser parser,
        IOptions<ParserOptions> options,
        TimeProvider timeProvider,
        ILogger<ParseOrchestrator> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _fileSystem = fileSystem;
        _parser = parser;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
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

    /// <summary>Wrapper um <see cref="RunOnceAsync"/>, der erwartbare Fehler frisst. Sichtbar fuer Tests via <c>InternalsVisibleTo</c>.</summary>
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
            // Defense-in-Depth. Falls eine Markdig-/Yaml-/JSON-Exception ueber den
            // ParseOneAsync-Catch hinausschluepft (z. B. aus einem Sub-Renderer oder einem
            // Frontmatter-Pfad), bleibt der Periodic-Tick-Loop am Leben — naechster Tick
            // versucht es erneut, ohne dass ein einziges kaputtes File den Service killt.
            LogPollFailed(_logger, ex);
        }
    }

    /// <summary>Führt einen kompletten Poll-Durchlauf aus. Sichtbar für Tests via <c>InternalsVisibleTo</c>.</summary>
    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        long startedAt = _timeProvider.GetTimestamp();
        int processed = 0;
        int skipped = 0;

        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            IMarkdownSourceProvider sourceProvider = scope.ServiceProvider.GetRequiredService<IMarkdownSourceProvider>();
            IMarkdownDocumentRepository docRepo = scope.ServiceProvider.GetRequiredService<IMarkdownDocumentRepository>();
            ITagRepository tagRepo = scope.ServiceProvider.GetRequiredService<ITagRepository>();

            List<MarkdownSourceSnapshot> batch = new(_options.BatchSize);
            await foreach (MarkdownSourceSnapshot snapshot in sourceProvider.EnumerateAsync(cancellationToken).ConfigureAwait(false))
            {
                batch.Add(snapshot);
                if (batch.Count >= _options.BatchSize)
                {
                    (int p, int s) = await ProcessBatchAsync(docRepo, tagRepo, batch, cancellationToken).ConfigureAwait(false);
                    processed += p;
                    skipped += s;
                    batch.Clear();
                }
            }
            if (batch.Count > 0)
            {
                (int p, int s) = await ProcessBatchAsync(docRepo, tagRepo, batch, cancellationToken).ConfigureAwait(false);
                processed += p;
                skipped += s;
            }
        }

        TimeSpan elapsed = _timeProvider.GetElapsedTime(startedAt);
        LogPollCompleted(_logger, processed, skipped, elapsed.TotalMilliseconds);
    }

    private async Task<(int Processed, int Skipped)> ProcessBatchAsync(
        IMarkdownDocumentRepository docRepo,
        ITagRepository tagRepo,
        List<MarkdownSourceSnapshot> batch,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, string> hashes = batch.ToDictionary(s => s.Id, s => s.ContentHash);
        IReadOnlyList<Guid> stale = await docRepo.GetStaleOrMissingAsync(hashes, cancellationToken).ConfigureAwait(false);
        if (stale.Count == 0)
        {
            return (0, batch.Count);
        }

        HashSet<Guid> staleSet = [.. stale];
        List<MarkdownSourceSnapshot> targets = [.. batch.Where(snapshot => staleSet.Contains(snapshot.Id))];

        List<ParsedEntry> results = await ParseInParallelAsync(targets, cancellationToken).ConfigureAwait(false);

        // Tag-Cache fuer den ganzen Batch — sonst ruft jede Datei einen frischen
        // GetBySlugsAsync auf und addiert denselben Slug doppelt; SaveChanges scheitert dann mit
        // SqliteException 19 (UNIQUE constraint failed: Tags.Slug). Wir sammeln die Slugs einmal,
        // resolven die existierenden Tags in einem einzigen Roundtrip und legen die fehlenden
        // genau einmal pro Slug an.
        Dictionary<string, Guid> slugToId = await EnsureTagsForBatchAsync(tagRepo, results, cancellationToken).ConfigureAwait(false);

        foreach (ParsedEntry entry in results)
        {
            await PersistDocumentAsync(docRepo, entry, cancellationToken).ConfigureAwait(false);
            await SyncFileTagLinksAsync(tagRepo, entry, slugToId, cancellationToken).ConfigureAwait(false);
        }

        _ = await docRepo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return (results.Count, batch.Count - results.Count);
    }

    private async Task<List<ParsedEntry>> ParseInParallelAsync(
        List<MarkdownSourceSnapshot> targets,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim semaphore = new(_options.MaxParallelism);
        try
        {
            List<ParsedEntry> results = [];
            Lock resultsLock = new();

            Task[] tasks = targets
                .Select(snapshot => ParseOneAsync(snapshot, semaphore, results, resultsLock, cancellationToken))
                .ToArray();

            await Task.WhenAll(tasks).ConfigureAwait(false);
            return results;
        }
        finally
        {
            semaphore.Dispose();
        }
    }

    private async Task ParseOneAsync(
        MarkdownSourceSnapshot snapshot,
        SemaphoreSlim semaphore,
        List<ParsedEntry> sink,
        Lock resultsLock,
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
                // Markdig wirft ArgumentException u. a. bei depth-limit-Verstoessen
                // (zu tief verschachtelte Emphasis/Listen). InvalidOperationException kommt
                // aus dem Yaml-/Frontmatter-Pfad. Beide sind Format-Fehler im Dokument —
                // Datei ueberspringen, restlicher Batch laeuft weiter.
                LogParseFailed(_logger, snapshot.AbsolutePath, ex);
                return;
            }

            ParsedEntry entry = new(snapshot, parseResult);
            lock (resultsLock)
            {
                sink.Add(entry);
            }
        }
        finally
        {
            _ = semaphore.Release();
        }
    }

    private async Task PersistDocumentAsync(
        IMarkdownDocumentRepository docRepo,
        ParsedEntry entry,
        CancellationToken cancellationToken)
    {
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        string frontmatterJson = JsonSerializer.Serialize(entry.Result.Frontmatter, JsonOptions);
        string outlinksJson = JsonSerializer.Serialize(entry.Result.OutlinkSlugs, JsonOptions);

        MarkdownDocument? existing = await docRepo.GetByMarkdownFileIdAsync(entry.Snapshot.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
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

    // Sammelt alle einzigartigen Tag-Slugs aus dem Batch, fragt vorhandene in einem
    // einzigen Roundtrip ab und legt fehlende genau einmal an. Liefert einen vollstaendigen
    // Slug→Id-Lookup, ueber den die per-File-Verlinkung deterministisch arbeitet.
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

    private readonly record struct ParsedEntry(MarkdownSourceSnapshot Snapshot, ParseResult Result);

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

    [LoggerMessage(EventId = 207, Level = LogLevel.Warning, Message = "Parser-Poll übersprungen — Datenbank-Spitze, naechster Periodic-Tick versucht es erneut.")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 208, Level = LogLevel.Error, Message = "ParseOrchestrator-Watchdog: unerwartete Exception aufgefangen, Service wird ordentlich beendet.")]
    private static partial void LogWatchdogTriggered(ILogger logger, Exception exception);
}
