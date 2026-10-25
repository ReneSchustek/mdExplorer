using System.Data.Common;
using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Hosting;
using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.Messaging;
using MdExplorer.TagCloud.Models;
using MdExplorer.TagCloud.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MdExplorer.TagCloud.Services;

/// <summary>
/// <see cref="BackgroundService"/>, der die Tag-Statistik periodisch im Hintergrund lädt
/// und nur dann eine <see cref="TagsRefreshedMessage"/> veröffentlicht, wenn sich die
/// Signatur (Tags × Counts × Last-Used-Zeitstempel) gegenüber dem letzten Snapshot
/// geändert hat. Damit folgt die UI dem Indexer/Parser ohne explizite Cross-Modul-Kopplung
/// und ohne unnötige UI-Refreshes.
/// </summary>
public sealed partial class TagCloudRefreshService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessenger _messenger;
    private readonly TimeProvider _timeProvider;
    private readonly TagCloudOptions _options;
    private readonly IBackgroundServiceStatus _serviceStatus;
    private readonly ILogger<TagCloudRefreshService> _logger;
    private int? _lastSignature;

    /// <summary>Erzeugt den Service und löst Pflichtabhängigkeiten auf.</summary>
    public TagCloudRefreshService(
        IServiceScopeFactory scopeFactory,
        IMessenger messenger,
        TimeProvider timeProvider,
        IOptions<TagCloudOptions> options,
        IBackgroundServiceStatus serviceStatus,
        ILogger<TagCloudRefreshService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _messenger = messenger;
        _timeProvider = timeProvider;
        _options = options.Value;
        ArgumentNullException.ThrowIfNull(serviceStatus);
        _serviceStatus = serviceStatus;
        _logger = logger;
    }

    /// <summary>Lädt einen Snapshot und veröffentlicht ihn nur bei Signatur-Wechsel.</summary>
    internal async Task PublishIfChangedAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TagStatistic> snapshot;
        try
        {
            snapshot = await LoadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or ArgumentException)
        {
            // Eine Datenbank-Spitze nach ausgeschöpften Wiederholungen oder ein unerwarteter Wert
            // aus der Abfrage kostet nur diesen Durchlauf; der nächste Takt versucht es erneut.
            LogRefreshFailure(_logger, exception);
            return;
        }
        int signature = ComputeSignature(snapshot);
        if (_lastSignature == signature)
        {
            return;
        }
        _lastSignature = signature;
        _ = _messenger.Send(new TagsRefreshedMessage(snapshot));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(_options.RefreshIntervalSeconds);
        LogRefreshStarted(_logger, interval.TotalSeconds);

        using PeriodicTimer timer = new(interval, _timeProvider);
        try
        {
            await PublishIfChangedAsync(stoppingToken).ConfigureAwait(false);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await PublishIfChangedAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host-Shutdown, kein Fehler.
        }
        catch (Exception ex) when (BackgroundServiceWatchdog.IsRecoverable(ex, stoppingToken))
        {
            // Was bis hierher durchschlägt, beendet den Dienst geordnet, statt den Prozess
            // mit einer unbehandelten Ausnahme abzureißen.
            LogWatchdogTriggered(_logger, ex);
            _serviceStatus.ReportStopped(BackgroundServiceKind.TagCloud);
        }
    }

    // Ein Hash statt eines Vergleichs der ganzen Liste. Eine Kollision unterdrückte schlimmstenfalls
    // eine Aktualisierung, bis sich die Statistik das nächste Mal ändert.
    private static int ComputeSignature(IReadOnlyList<TagStatistic> snapshot)
    {
        HashCode hash = default;
        hash.Add(snapshot.Count);
        foreach (TagStatistic statistic in snapshot)
        {
            hash.Add(statistic.Slug, StringComparer.Ordinal);
            hash.Add(statistic.Count);
            hash.Add(statistic.LastUsedUtc);
        }
        return hash.ToHashCode();
    }

    [LoggerMessage(EventId = 410, Level = LogLevel.Information, Message = "Tag-Cloud-Refresh aktiv, Intervall {IntervalSeconds:F1}s.")]
    private static partial void LogRefreshStarted(ILogger logger, double intervalSeconds);

    [LoggerMessage(EventId = 411, Level = LogLevel.Error, Message = "Tag-Cloud-Hintergrund-Refresh fehlgeschlagen.")]
    private static partial void LogRefreshFailure(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 412, Level = LogLevel.Error, Message = "TagCloudRefreshService-Watchdog: unerwartete Exception aufgefangen, Service wird ordentlich beendet.")]
    private static partial void LogWatchdogTriggered(ILogger logger, Exception exception);

    private async Task<IReadOnlyList<TagStatistic>> LoadSnapshotAsync(CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            ITagStatisticsService service = scope.ServiceProvider.GetRequiredService<ITagStatisticsService>();
            // Geladen wird immer die längere Liste; die Nachricht trägt sie ungekürzt zum ViewModel.
            int topN = Math.Max(_options.TopN, _options.LongTailTopN);
            return await service.GetTopTagsAsync(topN, cancellationToken).ConfigureAwait(false);
        }
    }
}
