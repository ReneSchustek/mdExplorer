using System.Data.Common;
using System.Linq;
using System.Text;
using System.Text.Json;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Hosting;
using MdExplorer.Core.Text;
using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MdExplorer.Search.Services;

/// <summary>
/// Hält den FTS5-Index <c>MarkdownSearchIndex</c> konsistent zum Parser-Output. Die Pflege erfolgt
/// als <see cref="BackgroundService"/> mit Polling-Strategie analog zum <c>ParseOrchestrator</c>.
/// Reine Application-Code-Pflege wurde der Trigger-Variante vorgezogen, weil der Body-Plaintext
/// nicht als Spalte in <c>MarkdownDocuments</c> liegt und SQLite-Trigger ihn aus dem GZip-HTML
/// nicht extrahieren können — eine UDF-/Spalten-Erweiterung wäre Cross-Modul-Eingriff in den Parser.
/// DELETE-Konsistenz übernimmt weiterhin der Trigger aus der FTS5-Migration. Der Zugriff auf
/// Quelldaten und Index-Storage läuft über Core-Abstraktionen — das Modul kennt weder EF noch SQLite.
/// </summary>
public sealed partial class Fts5IndexMaintainer : BackgroundService, ISearchIndexer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;
    private readonly SearchOptions _options;
    private readonly ILogger<Fts5IndexMaintainer> _logger;

    /// <summary>Erzeugt den Maintainer und löst Pflichtabhängigkeiten auf.</summary>
    public Fts5IndexMaintainer(
        IServiceScopeFactory scopeFactory,
        IFileSystem fileSystem,
        IOptions<SearchOptions> options,
        TimeProvider timeProvider,
        ILogger<Fts5IndexMaintainer> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _fileSystem = fileSystem;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(_options.IndexMaintenanceIntervalSeconds);
        LogMaintainerStarted(_logger, interval.TotalSeconds);

        try
        {
            await TrySynchronizeAsync(stoppingToken).ConfigureAwait(false);

            using PeriodicTimer timer = new(interval, _timeProvider);
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    return;
                }
                await TrySynchronizeAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // erwartete Abbruchsemantik
        }
        catch (Exception ex) when (BackgroundServiceWatchdog.IsRecoverable(ex))
        {
            // Letzte Schicht. Faengt unerwartete Exceptions ab, damit der Host
            // den Service ordentlich beendet statt unhandled-Crash.
            LogMaintainerWatchdogTriggered(_logger, ex);
        }
        finally
        {
            LogMaintainerStopped(_logger);
        }
    }

    /// <summary>Wrapper um <see cref="SynchronizeAsync"/>, der erwartbare Fehler frisst. Sichtbar fuer Tests via <c>InternalsVisibleTo</c>.</summary>
    internal async Task TrySynchronizeAsync(CancellationToken stoppingToken)
    {
        try
        {
            _ = await SynchronizeAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbException ex)
        {
            // SQLite-Spitze nach Retry-Budget darf den HostedService nicht killen —
            // der naechste Periodic-Tick erhaelt eine erneute Chance.
            LogSynchronizationFailed(_logger, ex);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Defense-in-Depth. Wenn eine korrupte Frontmatter-JSON oder ein
            // unerwarteter State im BuildUpserts-Pfad eine ArgumentException erzeugt,
            // bleibt der Periodic-Loop am Leben — naechster Tick versucht es erneut.
            LogSynchronizationFailed(_logger, ex);
        }
    }

    /// <inheritdoc />
    public async Task<int> SynchronizeAsync(CancellationToken cancellationToken)
    {
        long startedAt = _timeProvider.GetTimestamp();

        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            ISearchSourceProvider sourceProvider = scope.ServiceProvider.GetRequiredService<ISearchSourceProvider>();
            ISearchIndexStorage storage = scope.ServiceProvider.GetRequiredService<ISearchIndexStorage>();

            IReadOnlyDictionary<Guid, string> indexedHashes = await storage.LoadIndexedHashesAsync(cancellationToken).ConfigureAwait(false);
            SearchSourceData source = await sourceProvider.LoadAsync(cancellationToken).ConfigureAwait(false);

            SynchronizationDiff diff = ComputeDiff(indexedHashes, source);
            if (diff.Targets.Count == 0 && diff.Orphans.Count == 0)
            {
                return 0;
            }

            IReadOnlyList<SearchIndexEntry> upserts = await BuildUpsertsAsync(diff.Targets, source.TagsByFileId, cancellationToken).ConfigureAwait(false);
            await storage.ApplyChangesAsync(diff.Orphans, upserts, cancellationToken).ConfigureAwait(false);

            int changed = diff.Orphans.Count + upserts.Count;
            TimeSpan elapsed = _timeProvider.GetElapsedTime(startedAt);
            LogSynchronizationCompleted(_logger, changed, elapsed.TotalMilliseconds);
            return changed;
        }
    }

    /// <summary>
    /// Vergleicht den persistierten Index-Stand mit dem aktuellen Source-Snapshot und bestimmt
    /// (a) neu/zu aktualisierende Dokumente bis zur Batch-Grenze und (b) verwaiste Index-Einträge.
    /// </summary>
    private SynchronizationDiff ComputeDiff(
        IReadOnlyDictionary<Guid, string> indexedHashes,
        SearchSourceData source)
        => ComputeDiffCore(indexedHashes, source, _options.MaintenanceBatchSize);

    /// <summary>
    /// Pure-Function-Variante von <see cref="ComputeDiff"/>. Sichtbar für Direkt-Tests.
    /// </summary>
    internal static SynchronizationDiff ComputeDiffCore(
        IReadOnlyDictionary<Guid, string> indexedHashes,
        SearchSourceData source,
        int batchSize)
    {
        // Die liveIds muessen ueber den gesamten Source-Snapshot vollstaendig sein, sonst markiert
        // der Orphan-Pass weiter unten alle nicht erreichten Source-Eintraege faelschlich als
        // verwaist. Targets werden separat bis zum Batch-Limit beschnitten — die Iteration laeuft
        // dennoch weiter, damit liveIds komplett bleibt.
        HashSet<Guid> liveIds = new(source.Documents.Count);
        List<SearchSourceDocument> targets = new(batchSize);
        foreach (SearchSourceDocument document in source.Documents)
        {
            _ = liveIds.Add(document.MarkdownFileId);
            if (targets.Count >= batchSize)
            {
                continue;
            }
            if (!indexedHashes.TryGetValue(document.MarkdownFileId, out string? existingHash)
                || !string.Equals(existingHash, document.SourceContentHash, StringComparison.Ordinal))
            {
                targets.Add(document);
            }
        }

        List<Guid> orphans = indexedHashes.Keys
            .Where(indexedId => !liveIds.Contains(indexedId))
            .ToList();

        return new SynchronizationDiff(targets, orphans);
    }

    private async Task<IReadOnlyList<SearchIndexEntry>> BuildUpsertsAsync(
        IReadOnlyList<SearchSourceDocument> targets,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> tagsByFileId,
        CancellationToken cancellationToken)
    {
        List<SearchIndexEntry> upserts = new(targets.Count);
        foreach (SearchSourceDocument target in targets)
        {
            string body = await ReadBodyAsync(target.AbsolutePath, cancellationToken).ConfigureAwait(false);
            string tags = JoinTags(tagsByFileId, target.MarkdownFileId);
            string frontmatter = FlattenFrontmatter(target.FrontmatterJson);
            upserts.Add(new SearchIndexEntry(
                target.MarkdownFileId,
                target.Title,
                body,
                tags,
                frontmatter,
                target.RelativePath,
                target.SourceContentHash));
        }
        return upserts;
    }

    internal readonly record struct SynchronizationDiff(
        IReadOnlyList<SearchSourceDocument> Targets,
        IReadOnlyList<Guid> Orphans);

    private static string JoinTags(IReadOnlyDictionary<Guid, IReadOnlyList<string>> tagsByFileId, Guid markdownFileId)
    {
        if (!tagsByFileId.TryGetValue(markdownFileId, out IReadOnlyList<string>? tagNames) || tagNames.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        foreach (string name in tagNames)
        {
            if (builder.Length > 0)
            {
                _ = builder.Append(' ');
            }
            _ = builder.Append(name);
        }
        return builder.ToString();
    }

    private async Task<string> ReadBodyAsync(string absolutePath, CancellationToken cancellationToken)
    {
        try
        {
            byte[] bytes = await _fileSystem.ReadAllBytesAsync(absolutePath, cancellationToken).ConfigureAwait(false);
            string source = Utf8Decoder.DecodeNoBom(bytes);
            return StripFrontmatter(source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            LogReadFailed(_logger, absolutePath, ex);
            return string.Empty;
        }
    }

    internal static string StripFrontmatter(string source)
    {
        if (!source.StartsWith("---", StringComparison.Ordinal))
        {
            return source;
        }
        int firstLineBreak = source.IndexOf('\n', 3);
        if (firstLineBreak < 0)
        {
            return source;
        }
        int closingIndex = source.IndexOf("\n---", firstLineBreak, StringComparison.Ordinal);
        if (closingIndex < 0)
        {
            return source;
        }
        int afterClosingMarker = closingIndex + 4;
        if (afterClosingMarker >= source.Length)
        {
            return string.Empty;
        }
        int newlineAfter = source.IndexOf('\n', afterClosingMarker);
        return newlineAfter < 0 ? string.Empty : source[(newlineAfter + 1)..];
    }

    internal static string FlattenFrontmatter(string frontmatterJson)
    {
        if (string.IsNullOrWhiteSpace(frontmatterJson))
        {
            return string.Empty;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(frontmatterJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            StringBuilder builder = new();
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (builder.Length > 0)
                {
                    _ = builder.Append(' ');
                }
                _ = builder.Append(property.Name);
                _ = builder.Append(' ');
                _ = builder.Append(property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString());
            }
            return builder.ToString();
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    [LoggerMessage(EventId = 300, Level = LogLevel.Information, Message = "FTS5-Maintainer gestartet — Polling alle {IntervalSeconds:F0} s.")]
    private static partial void LogMaintainerStarted(ILogger logger, double intervalSeconds);

    [LoggerMessage(EventId = 301, Level = LogLevel.Information, Message = "FTS5-Maintainer gestoppt.")]
    private static partial void LogMaintainerStopped(ILogger logger);

    [LoggerMessage(EventId = 302, Level = LogLevel.Debug, Message = "FTS5-Synchronisation abgeschlossen — {Changed} Änderung(en) in {ElapsedMs:F0} ms.")]
    private static partial void LogSynchronizationCompleted(ILogger logger, int changed, double elapsedMs);

    [LoggerMessage(EventId = 303, Level = LogLevel.Warning, Message = "Quelldatei konnte nicht gelesen werden: {Path}")]
    private static partial void LogReadFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 304, Level = LogLevel.Warning, Message = "FTS5-Synchronisation übersprungen — Datenbank-Spitze, naechster Periodic-Tick versucht es erneut.")]
    private static partial void LogSynchronizationFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 305, Level = LogLevel.Error, Message = "Fts5IndexMaintainer-Watchdog: unerwartete Exception aufgefangen, Service wird ordentlich beendet.")]
    private static partial void LogMaintainerWatchdogTriggered(ILogger logger, Exception exception);
}
