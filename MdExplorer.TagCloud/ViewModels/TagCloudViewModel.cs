using System.Collections.ObjectModel;
using System.Data.Common;
using System.Linq;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.Messaging;
using MdExplorer.TagCloud.Models;
using MdExplorer.TagCloud.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MdExplorer.TagCloud.ViewModels;

/// <summary>
/// ViewModel der Tag-Cloud. Hält die aktuelle Top-N-Snapshot-Liste, exponiert Sortier-Option
/// und Long-Tail-Toggle und veröffentlicht <see cref="TagClickedMessage"/> über den
/// <see cref="IMessenger"/>. Empfängt Hintergrund-Aktualisierungen über
/// <see cref="TagsRefreshedMessage"/> — dafür ist die Items-Collection mit
/// <see cref="BindingOperations.EnableCollectionSynchronization(System.Collections.IEnumerable, object)"/>
/// thread-sicher angemeldet.
/// </summary>
public sealed partial class TagCloudViewModel : ObservableObject, IDisposable,
    IRecipient<TagsRefreshedMessage>
{
    private readonly ITagStatisticsService _statisticsService;
    private readonly IMessenger _messenger;
    private readonly TagCloudOptions _options;
    private readonly ILogger<TagCloudViewModel> _logger;
    private readonly object _itemsGate = new();

    private IReadOnlyList<TagStatistic> _latestSnapshot;
    private bool _disposed;

    [ObservableProperty]
    private TagCloudSortOption _sort = TagCloudSortOption.Frequency;

    [ObservableProperty]
    private bool _isLongTailExpanded;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _minCount = 1;

    [ObservableProperty]
    private int _maxCount = 1;

    /// <summary>Erzeugt das ViewModel und registriert es beim Messenger.</summary>
    public TagCloudViewModel(
        ITagStatisticsService statisticsService,
        IMessenger messenger,
        IOptions<TagCloudOptions> options,
        ILogger<TagCloudViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(statisticsService);
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _statisticsService = statisticsService;
        _messenger = messenger;
        _options = options.Value;
        _logger = logger;
        _latestSnapshot = Array.Empty<TagStatistic>();
        Items = [];
        BindingOperations.EnableCollectionSynchronization(Items, _itemsGate);
        _messenger.RegisterAll(this);
    }

    /// <summary>Sichtbare Tag-Items in aktueller Sortierreihenfolge.</summary>
    public ObservableCollection<TagItemViewModel> Items { get; }

    /// <summary>Aktuelle Top-N-Grenze (Default oder Long-Tail).</summary>
    public int EffectiveTopN => IsLongTailExpanded ? _options.LongTailTopN : _options.TopN;

    /// <summary>Lädt eine neue Snapshot-Liste synchron-async vom Service.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            IReadOnlyList<TagStatistic> snapshot = await _statisticsService
                .GetTopTagsAsync(EffectiveTopN, cancellationToken)
                .ConfigureAwait(false);
            ApplySnapshot(snapshot);
        }
        catch (OperationCanceledException)
        {
            // Refresh wurde abgebrochen — kein Fehler.
        }
        catch (InvalidOperationException exception)
        {
            LogRefreshFailure(_logger, exception);
        }
        catch (DbException exception)
        {
            // SQLite-Spitze nach Retry-Budget — UI bleibt auf vorigem Stand.
            LogRefreshFailure(_logger, exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Empfängt Hintergrund-Snapshots vom Refresh-Service. Wird vom Messenger aufgerufen.
    /// </summary>
    public void Receive(TagsRefreshedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ApplySnapshot(message.Snapshot);
    }

    /// <summary>Veröffentlicht ein <see cref="TagClickedMessage"/> für den gewählten Tag.</summary>
    public void HandleTagClicked(TagItemViewModel item, TagFilterMode mode)
    {
        ArgumentNullException.ThrowIfNull(item);
        TagClickedMessage message = new(item.Slug, item.Name, mode);
        LogTagClicked(_logger, item.Slug, mode);
        _ = _messenger.Send(message);
    }

    /// <summary>Wendet einen Snapshot an — sortiert und ersetzt die UI-Items unter Lock.</summary>
    internal void ApplySnapshot(IReadOnlyList<TagStatistic> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _latestSnapshot = snapshot;

        TagStatistic[] sorted = SortSnapshot(snapshot, Sort);
        lock (_itemsGate)
        {
            Items.Clear();
            foreach (TagStatistic statistic in sorted)
            {
                Items.Add(new TagItemViewModel(statistic));
            }
        }
        UpdateCountRange(sorted);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _messenger.UnregisterAll(this);
    }

    partial void OnSortChanged(TagCloudSortOption value)
    {
        ApplySnapshot(_latestSnapshot);
    }

    partial void OnIsLongTailExpandedChanged(bool value)
    {
        _ = RefreshAsync(CancellationToken.None);
    }

    private void UpdateCountRange(TagStatistic[] sorted)
    {
        if (sorted.Length == 0)
        {
            MinCount = 1;
            MaxCount = 1;
            return;
        }
        int[] counts = sorted.Select(statistic => statistic.Count).ToArray();
        MinCount = Math.Max(1, counts.Min());
        MaxCount = Math.Max(MinCount, counts.Max());
    }

    private static TagStatistic[] SortSnapshot(IReadOnlyList<TagStatistic> snapshot, TagCloudSortOption sort)
    {
        switch (sort)
        {
            case TagCloudSortOption.Alphabetical:
                return snapshot
                    .OrderBy(stat => stat.Slug, StringComparer.Ordinal)
                    .ToArray();
            case TagCloudSortOption.RecentlyUsed:
                return snapshot
                    .OrderByDescending(stat => stat.LastUsedUtc)
                    .ThenBy(stat => stat.Slug, StringComparer.Ordinal)
                    .ToArray();
            case TagCloudSortOption.Frequency:
            default:
                return snapshot
                    .OrderByDescending(stat => stat.Count)
                    .ThenBy(stat => stat.Slug, StringComparer.Ordinal)
                    .ToArray();
        }
    }

    [LoggerMessage(EventId = 400, Level = LogLevel.Information, Message = "Tag {Slug} angeklickt im Modus {Mode}.")]
    private static partial void LogTagClicked(ILogger logger, string slug, TagFilterMode mode);

    [LoggerMessage(EventId = 401, Level = LogLevel.Error, Message = "Tag-Cloud-Refresh fehlgeschlagen.")]
    private static partial void LogRefreshFailure(ILogger logger, Exception exception);
}
