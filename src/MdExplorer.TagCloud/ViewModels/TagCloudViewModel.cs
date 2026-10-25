using System.Data.Common;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.Core.Hosting;
using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.Messaging;
using MdExplorer.TagCloud.Models;
using MdExplorer.TagCloud.Options;
using MdExplorer.TagCloud.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MdExplorer.TagCloud.ViewModels;

/// <summary>
/// ViewModel der Tag-Cloud. Hält die aktuelle Top-N-Snapshot-Liste, exponiert Sortier-Option
/// und Long-Tail-Toggle und veröffentlicht <see cref="TagClickedMessage"/> über den
/// <see cref="IMessenger"/>. Empfängt Hintergrund-Aktualisierungen über
/// <see cref="TagsRefreshedMessage"/> und wendet sie auf dem UI-Thread an. Jeder Snapshot
/// ersetzt die Liste als Ganzes: Eine einzelne Änderungsmeldung statt einer je Schlagwort.
/// </summary>
public sealed partial class TagCloudViewModel : ObservableObject, IDisposable,
    IRecipient<TagsRefreshedMessage>
{
    private readonly ITagStatisticsService _statisticsService;
    private readonly IMessenger _messenger;
    private readonly TagCloudOptions _options;
    private readonly ILogger<TagCloudViewModel> _logger;

    // Der Kontext des Threads, der das ViewModel anlegt, in der Anwendung der UI-Thread. Snapshots
    // des Hintergrunddienstes kommen darüber auf ihn, damit Items und Zählgrenzen nur dort wechseln.
    private readonly SynchronizationContext? _uiContext;

    private IReadOnlyList<TagStatistic> _latestSnapshot;
    private CancellationTokenSource? _refreshCancellation;
    private int _refreshGeneration;
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

    /// <summary>Sichtbare Tag-Items in aktueller Sortierreihenfolge.</summary>
    [ObservableProperty]
    private IReadOnlyList<TagItemViewModel> _items = [];

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
        _uiContext = SynchronizationContext.Current;
        _messenger.RegisterAll(this);
    }

    /// <summary>Aktuelle Top-N-Grenze (Default oder Long-Tail).</summary>
    public int EffectiveTopN => IsLongTailExpanded ? _options.LongTailTopN : _options.TopN;

    /// <summary>Schriftgröße des seltensten Schlagworts in DIP, aus der Konfiguration.</summary>
    public double MinFontSize => _options.MinFontSize;

    /// <summary>Schriftgröße des häufigsten Schlagworts in DIP, aus der Konfiguration.</summary>
    public double MaxFontSize => _options.MaxFontSize;

    /// <summary>Lädt eine neue Snapshot-Liste vom Service; bei einem Fehler bleibt der bisherige Stand stehen.</summary>
    /// <remarks>
    /// Ein neuer Aufruf bricht den vorigen ab, und nur der jüngste darf sein Ergebnis anzeigen:
    /// Aufklappen und gleich wieder Zuklappen zeigte sonst die lange Liste, wenn ihr Abruf zuletzt
    /// endete.
    /// </remarks>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        int generation = await SupersedePreviousRefreshAsync(run).ConfigureAwait(true);
        IsBusy = true;
        try
        {
            IReadOnlyList<TagStatistic> snapshot = await _statisticsService
                .GetTopTagsAsync(EffectiveTopN, run.Token)
                .ConfigureAwait(true);
            if (IsCurrentRefresh(generation))
            {
                ApplySnapshot(snapshot);
            }
        }
        catch (OperationCanceledException)
        {
            // Abgebrochen, von außen oder durch einen neueren Abruf; kein Fehler.
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            // Eine Datenbank-Spitze nach ausgeschöpften Wiederholungen; die Anzeige bleibt auf dem vorigen Stand.
            LogRefreshFailure(_logger, exception);
        }
        finally
        {
            EndRefresh(run, generation);
        }
    }

    /// <summary>
    /// Empfängt Hintergrund-Snapshots vom Refresh-Service. Wird vom Messenger aufgerufen.
    /// </summary>
    public void Receive(TagsRefreshedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Der Hintergrunddienst lädt die lange Liste. Angezeigt wird davon nur, was der aktuelle
        // Zustand vorsieht; zugeklappt sind das die häufigsten. Gekürzt wird nach Häufigkeit, nicht
        // nach der gewählten Sortierung: Sonst zeigte „Alphabetisch" die ersten Buchstaben statt
        // der wichtigsten Schlagworte. Die Nachricht kommt vom Hintergrund; angewendet wird auf dem
        // UI-Thread.
        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            ApplySnapshot(TopByCount(message.Snapshot, EffectiveTopN));
            return;
        }
        _uiContext.Post(_ => ApplySnapshot(TopByCount(message.Snapshot, EffectiveTopN)), state: null);
    }

    /// <summary>Veröffentlicht ein <see cref="TagClickedMessage"/> für den gewählten Tag.</summary>
    public void HandleTagClicked(TagItemViewModel item, TagFilterMode mode)
    {
        ArgumentNullException.ThrowIfNull(item);
        TagClickedMessage message = new(item.Slug, item.Name, mode);
        LogTagClicked(_logger, item.Slug, mode);
        _ = _messenger.Send(message);
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
        Interlocked.Exchange(ref _refreshCancellation, null)?.Cancel();
    }

    /// <summary>Wendet einen Snapshot an: sortiert, Schriftgrößen gerechnet, Liste ersetzt.</summary>
    internal void ApplySnapshot(IReadOnlyList<TagStatistic> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _latestSnapshot = snapshot;

        TagStatistic[] sorted = SortSnapshot(snapshot, Sort);
        UpdateCountRange(sorted);
        Items =
        [
            .. sorted.Select(statistic => new TagItemViewModel(
                statistic,
                TagFontSizeCalculator.Compute(Math.Max(0, statistic.Count), MinCount, MaxCount, MinFontSize, MaxFontSize))),
        ];
    }

    private static TagStatistic[] TopByCount(IReadOnlyList<TagStatistic> snapshot, int topN) =>
        [.. snapshot
            .OrderByDescending(stat => stat.Count)
            .ThenBy(stat => stat.Slug, StringComparer.Ordinal)
            .Take(topN)];

    private static TagStatistic[] SortSnapshot(IReadOnlyList<TagStatistic> snapshot, TagCloudSortOption sort)
    {
        switch (sort)
        {
            case TagCloudSortOption.Alphabetical:
                // Nach der Sprache der Oberfläche: Ordinal stünde „äpfel“ hinter „zebra“.
                return snapshot
                    .OrderBy(stat => stat.Name, StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true))
                    .ThenBy(stat => stat.Slug, StringComparer.Ordinal)
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

    partial void OnSortChanged(TagCloudSortOption value)
    {
        ApplySnapshot(_latestSnapshot);
    }

    partial void OnIsLongTailExpandedChanged(bool value)
    {
        // Ohne await: Das Umschalten wartet nicht auf die Datenbank. Was RefreshAsync nicht selbst
        // behandelt, protokolliert RefreshUnobservedAsync, statt es unbeobachtet liegen zu lassen.
        _ = RefreshUnobservedAsync();
    }

    private async Task RefreshUnobservedAsync()
    {
        try
        {
            await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception) when (BackgroundServiceWatchdog.IsRecoverable(exception, CancellationToken.None))
        {
            LogRefreshFailure(_logger, exception);
        }
    }

    // Bricht den laufenden Abruf ab und liefert die Nummer des neuen.
    private async Task<int> SupersedePreviousRefreshAsync(CancellationTokenSource run)
    {
        int generation = Interlocked.Increment(ref _refreshGeneration);
        CancellationTokenSource? previous = Interlocked.Exchange(ref _refreshCancellation, run);
        if (previous is not null)
        {
            await previous.CancelAsync().ConfigureAwait(true);
        }
        return generation;
    }

    private bool IsCurrentRefresh(int generation) => generation == Volatile.Read(ref _refreshGeneration);

    // Nur der jüngste Abruf nimmt die Busy-Anzeige zurück; ein überholter liefe sonst dem neuen dazwischen.
    private void EndRefresh(CancellationTokenSource run, int generation)
    {
        _ = Interlocked.CompareExchange(ref _refreshCancellation, null, run);
        if (IsCurrentRefresh(generation))
        {
            IsBusy = false;
        }
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
}
