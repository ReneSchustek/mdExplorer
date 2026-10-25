using System.Data.Common;
using System.Linq;
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
/// nicht extrahieren können; eine UDF-/Spalten-Erweiterung wäre Cross-Modul-Eingriff in den Parser.
/// Auch das Entfernen verwaister Einträge erledigt der Abgleich selbst: Lösch-Trigger auf der
/// FTS5-Tabelle gibt es nicht, weil jede Bedingung auf der <c>UNINDEXED</c>-Spalte die ganze
/// Tabelle liest. Der Zugriff auf
/// Quelldaten und Index-Storage läuft über Core-Abstraktionen. Das Modul kennt weder EF noch SQLite.
/// </summary>
public sealed partial class Fts5IndexMaintainer : BackgroundService
{
    /// <summary>
    /// Hash, unter dem ein Eintrag ohne gelesenen Text im Index steht.
    /// </summary>
    /// <remarks>
    /// Er weicht von jedem echten Inhalts-Hash ab. Der Abgleich hält den Eintrag dadurch für
    /// veraltet und versucht die Datei erneut, auch wenn sich ihr Inhalt nicht ändert: Eine Sperre
    /// endet meist, ohne dass jemand die Datei bearbeitet.
    /// </remarks>
    internal const string UnreadBodyHash = "unread";

    // Wartezeit bis zum nächsten Versuch an einer Datei, die sich nicht lesen ließ. Der Wert ist
    // gesetzt, nicht gemessen: lang genug, dass eine dauerhaft gesperrte Datei nicht jeden Takt
    // gelesen wird, kurz genug, dass der Text nach dem Ende einer Sperre bald durchsuchbar ist.
    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMinutes(5);

    private readonly ReadRetryTracker _readRetries;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;
    private readonly SearchOptions _options;
    private readonly IBackgroundServiceStatus _serviceStatus;
    private readonly ILogger<Fts5IndexMaintainer> _logger;

    // Der Änderungsmarker nach dem letzten Abgleich, der nichts mehr zu tun fand. Solange er gleich
    // bleibt und keine Datei auf einen Leseversuch wartet, entfällt der volle Abgleich.
    private SearchSourceVersion? _settledVersion;

    /// <summary>Erzeugt den Maintainer und löst Pflichtabhängigkeiten auf.</summary>
    public Fts5IndexMaintainer(
        IServiceScopeFactory scopeFactory,
        IFileSystem fileSystem,
        IOptions<SearchOptions> options,
        TimeProvider timeProvider,
        IBackgroundServiceStatus serviceStatus,
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
        _readRetries = new ReadRetryTracker(timeProvider, ReadRetryDelay);
        ArgumentNullException.ThrowIfNull(serviceStatus);
        _serviceStatus = serviceStatus;
        _logger = logger;
    }

    /// <summary>
    /// Gleicht den Volltextindex einmal mit den geparsten Dokumenten ab. Läuft sonst im Takt des
    /// Hintergrunddienstes; öffentlich für Tests und das Messprojekt, die einen Abgleich gezielt
    /// auslösen.
    /// </summary>
    /// <param name="cancellationToken">Abbruch.</param>
    /// <returns>Anzahl der geschriebenen oder entfernten Einträge.</returns>
    public async Task<int> SynchronizeAsync(CancellationToken cancellationToken)
    {
        long startedAt = _timeProvider.GetTimestamp();

        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            ISearchSourceProvider sourceProvider = scope.ServiceProvider.GetRequiredService<ISearchSourceProvider>();
            ISearchIndexStorage storage = scope.ServiceProvider.GetRequiredService<ISearchIndexStorage>();

            SearchSourceVersion version = await sourceProvider.LoadVersionAsync(cancellationToken).ConfigureAwait(false);
            if (version == _settledVersion && !_readRetries.HasPending)
            {
                return 0;
            }

            IReadOnlyDictionary<Guid, string> indexedHashes = await storage.LoadIndexedHashesAsync(cancellationToken).ConfigureAwait(false);
            SearchSourceData source = await sourceProvider.LoadAsync(cancellationToken).ConfigureAwait(false);

            SynchronizationDiff diff = ComputeDiffCore(indexedHashes, source, _options.MaintenanceBatchSize, _readRetries.IsWaiting);
            _readRetries.ForgetOtherThan(diff.LiveIds);
            if (diff.Targets.Count == 0 && diff.Orphans.Count == 0)
            {
                _settledVersion = version;
                return 0;
            }
            _settledVersion = null;

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
    /// Ohne Abhängigkeiten, damit Tests den Rechenkern direkt aufrufen können.
    /// </summary>
    /// <remarks>
    /// Ein Dokument, das <paramref name="isWaitingForRetry"/> zurückhält, zählt nicht gegen die
    /// Stapelgrenze. Sonst hielten genug gesperrte Dateien, die jeden Takt wieder als veraltet
    /// erscheinen, alle anderen Dokumente dauerhaft aus dem Index.
    /// </remarks>
    internal static SynchronizationDiff ComputeDiffCore(
        IReadOnlyDictionary<Guid, string> indexedHashes,
        SearchSourceData source,
        int batchSize,
        Func<SearchSourceDocument, bool> isWaitingForRetry)
    {
        // liveIds deckt den ganzen Bestand ab, auch hinter der Stapelgrenze; sonst sähen alle nicht
        // erreichten Dokumente unten wie verwaiste Einträge aus.
        HashSet<Guid> liveIds = new(source.Documents.Count);
        List<SearchSourceDocument> targets = new(batchSize);
        foreach (SearchSourceDocument document in source.Documents)
        {
            _ = liveIds.Add(document.MarkdownFileId);
            if (targets.Count >= batchSize)
            {
                continue;
            }
            bool stale = !indexedHashes.TryGetValue(document.MarkdownFileId, out string? existingHash)
                || !string.Equals(existingHash, document.SourceContentHash, StringComparison.Ordinal);
            if (stale && !isWaitingForRetry(document))
            {
                targets.Add(document);
            }
        }

        List<Guid> orphans = [.. indexedHashes.Keys.Where(indexedId => !liveIds.Contains(indexedId))];

        return new SynchronizationDiff(targets, orphans, liveIds);
    }

    /// <summary>Wrapper um <see cref="SynchronizeAsync"/>, der erwartbare Fehler frisst. Sichtbar für Tests via <c>InternalsVisibleTo</c>.</summary>
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
            // Eine SQLite-Spitze nach ausgeschöpften Wiederholungen beendet den Dienst nicht. Der
            // nächste Takt versucht es erneut.
            LogSynchronizationFailed(_logger, ex);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Erzeugt ein unerwarteter Zustand beim Aufbau der Einträge eine dieser Ausnahmen,
            // bleibt die Schleife am Leben; der nächste Takt versucht es erneut.
            LogSynchronizationInconsistent(_logger, ex);
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(_options.IndexMaintenanceIntervalSeconds);
        LogMaintainerStarted(_logger, interval.TotalSeconds);

        try
        {
            await RunLoopAsync(interval, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // erwartete Abbruchsemantik
        }
        catch (Exception ex) when (BackgroundServiceWatchdog.IsRecoverable(ex, stoppingToken))
        {
            // Was bis hierher durchschlägt, beendet den Dienst geordnet, statt den Prozess
            // mit einer unbehandelten Ausnahme abzureißen.
            LogMaintainerWatchdogTriggered(_logger, ex);
            _serviceStatus.ReportStopped(BackgroundServiceKind.SearchIndex);
        }
        finally
        {
            LogMaintainerStopped(_logger);
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

    [LoggerMessage(EventId = 304, Level = LogLevel.Warning, Message = "FTS5-Synchronisation übersprungen — Datenbank-Spitze, nächster Periodic-Tick versucht es erneut.")]
    private static partial void LogSynchronizationFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 305, Level = LogLevel.Error, Message = "Fts5IndexMaintainer-Watchdog: unerwartete Exception aufgefangen, Service wird ordentlich beendet.")]
    private static partial void LogMaintainerWatchdogTriggered(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 306, Level = LogLevel.Debug, Message = "Quelldatei weiterhin nicht lesbar: {Path} — {Reason}")]
    private static partial void LogReadFailedAgain(ILogger logger, string path, string reason);

    [LoggerMessage(EventId = 307, Level = LogLevel.Warning, Message = "FTS5-Synchronisation übersprungen — unerwarteter Zustand beim Aufbau der Einträge, nächster Takt versucht es erneut.")]
    private static partial void LogSynchronizationInconsistent(ILogger logger, Exception exception);

    private async Task<IReadOnlyList<SearchIndexEntry>> BuildUpsertsAsync(
        IReadOnlyList<SearchSourceDocument> targets,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> tagsByFileId,
        CancellationToken cancellationToken)
    {
        List<SearchIndexEntry> upserts = new(targets.Count);
        foreach (SearchSourceDocument target in targets)
        {
            // Lässt sich die Datei nicht lesen, kommt sie trotzdem in den Index: Titel, Tags und
            // Pfad bleiben durchsuchbar, nur der Text fehlt bis zum nächsten Versuch.
            string? body = await ReadBodyAsync(target, cancellationToken).ConfigureAwait(false);
            string tags = SearchIndexText.JoinTags(tagsByFileId, target.MarkdownFileId);
            string frontmatter = SearchIndexText.FlattenFrontmatter(target.FrontmatterJson);
            upserts.Add(new SearchIndexEntry(
                target.MarkdownFileId,
                target.Title,
                body ?? string.Empty,
                tags,
                frontmatter,
                target.RelativePath,
                body is null ? UnreadBodyHash : target.SourceContentHash));
        }
        return upserts;
    }

    // Der Zeitgeber entsteht vor dem ersten Lauf: Der Takt hängt dann nicht davon ab, wie lange der
    // erste Abgleich über einen großen Bestand dauert.
    private async Task RunLoopAsync(TimeSpan interval, CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(interval, _timeProvider);
        await TrySynchronizeAsync(stoppingToken).ConfigureAwait(false);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await TrySynchronizeAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>Liest den Text einer Quelldatei ohne Frontmatter.</summary>
    /// <returns>Der Text, oder <see langword="null"/>, wenn sich die Datei nicht lesen ließ.</returns>
    private async Task<string?> ReadBodyAsync(SearchSourceDocument target, CancellationToken cancellationToken)
    {
        try
        {
            byte[] bytes = await _fileSystem.ReadAllBytesAsync(target.AbsolutePath, cancellationToken).ConfigureAwait(false);
            _readRetries.Succeeded(target.MarkdownFileId);
            return SearchIndexText.StripFrontmatter(Utf8Decoder.DecodeNoBom(bytes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RecordReadFailure(target, ex);
            return null;
        }
    }

    /// <summary>
    /// Vermerkt den Lesefehler für den nächsten Versuch und protokolliert ihn, mit vollem Stapel
    /// nur beim ersten Mal je Datei und Inhalt.
    /// </summary>
    /// <remarks>
    /// Eine dauerhaft gesperrte Datei wird alle <see cref="ReadRetryDelay"/> neu versucht. Stünde
    /// jeder Versuch mit Stapel im Protokoll, verdrängte eine einzige Datei mit der Zeit jede andere
    /// Meldung; die Wiederholungen stehen deshalb als eine Zeile auf Debug-Ebene.
    /// </remarks>
    private void RecordReadFailure(SearchSourceDocument target, Exception exception)
    {
        if (_readRetries.Failed(target))
        {
            LogReadFailedAgain(_logger, target.AbsolutePath, exception.Message);
            return;
        }
        LogReadFailed(_logger, target.AbsolutePath, exception);
    }

}
