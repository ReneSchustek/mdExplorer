using System.Data.Common;
using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.Messaging;
using MdExplorer.TagCloud.Models;
using MdExplorer.TagCloud.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;
using TagCloudOptions = MdExplorer.TagCloud.Options.TagCloudOptions;
using MdExplorer.TagCloud.Tests.Fakes;

namespace MdExplorer.TagCloud.Tests.ViewModels;

/// <summary>
/// Prüft den Ladeweg der Tag-Cloud: <see cref="TagCloudViewModel.RefreshAsync"/> mitsamt
/// Beschäftigt-Anzeige, Long-Tail-Umschaltung und Fehlerverhalten. Der Fehlerpfad ist der
/// wichtigste Teil: Bei einer Datenbank-Spitze muss die bereits angezeigte Wolke stehen
/// bleiben, sonst ist sie bei jeder kurzen Sperre der Datenbank leer.
/// </summary>
public sealed class TagCloudViewModelRefreshTests
{
    private static readonly DateTime FixedTime = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RefreshAsync_OnResult_FillsItemsAndCountRange()
    {
        ControllableStatistics service = new();
        service.Result =
        [
            new TagStatistic("Bericht", "bericht", 9, FixedTime),
            new TagStatistic("Notiz", "notiz", 3, FixedTime),
        ];
        using TagCloudViewModel sut = CreateViewModel(service);

        await sut.RefreshAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(2, sut.Items.Count);
        Assert.Equal("bericht", sut.Items[0].Slug, StringComparer.Ordinal);
        Assert.Equal(3, sut.MinCount);
        Assert.Equal(9, sut.MaxCount);
    }

    [Fact]
    public async Task RefreshAsync_OnCompletion_ClearsTheBusyFlag()
    {
        ControllableStatistics service = new();
        using TagCloudViewModel sut = CreateViewModel(service);

        await sut.RefreshAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task RefreshAsync_WhileLoading_SetsTheBusyFlag()
    {
        // Ohne die Anzeige wirkt eine langsame Abfrage wie eine eingefrorene Oberfläche.
        ControllableStatistics service = new();
        _ = service.Block();
        using TagCloudViewModel sut = CreateViewModel(service);

        Task running = sut.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.True(sut.IsBusy);
        service.Release();
        await running.ConfigureAwait(true);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task RefreshAsync_ByDefault_RequestsTheStandardTopN()
    {
        ControllableStatistics service = new();
        using TagCloudViewModel sut = CreateViewModel(service, new TagCloudOptions { TopN = 25, LongTailTopN = 700 });

        await sut.RefreshAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(25, service.LastTopN);
    }

    [Fact]
    public async Task RefreshAsync_WithExpandedLongTail_RequestsTheLargerTopN()
    {
        ControllableStatistics service = new();
        using TagCloudViewModel sut = CreateViewModel(service, new TagCloudOptions { TopN = 25, LongTailTopN = 700 });
        sut.IsLongTailExpanded = true;

        await sut.RefreshAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(700, service.LastTopN);
        Assert.Equal(700, sut.EffectiveTopN);
    }

    [Fact]
    public async Task ExpandingTheLongTail_TriggersARefreshOnItsOwn()
    {
        // Das Umschalten ist die einzige Bedienhandlung, die selbst nachladen muss,
        // sonst zeigt die aufgeklappte Wolke weiter nur die Top-N.
        ControllableStatistics service = new();
        using TagCloudViewModel sut = CreateViewModel(service);
        int callsBefore = service.CallCount;

        sut.IsLongTailExpanded = true;

        Assert.True(
            await service.Calls.WaitForAsync(callsBefore + 1, TimeSpan.FromSeconds(5)).ConfigureAwait(true),
            "Das Aufklappen hat keine Aktualisierung ausgelöst.");
    }

    [Fact]
    public async Task RefreshAsync_WhenAnOlderCallEndsLast_KeepsTheNewerResult()
    {
        // Aufklappen und gleich wieder Zuklappen: Endet der lange Abruf zuletzt, darf er die kurze
        // Liste nicht überschreiben.
        ControllableStatistics service = new();
        using TagCloudViewModel sut = CreateViewModel(service);
        service.Result = [Stat("a", 5), Stat("b", 4), Stat("c", 3)];
        TaskCompletionSource older = service.Block();
        int callsBefore = service.CallCount;
        Task olderRefresh = sut.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.True(await service.Calls.WaitForAsync(callsBefore + 1, TimeSpan.FromSeconds(5)).ConfigureAwait(true));

        service.Unblock();
        service.Result = [Stat("a", 5)];
        await sut.RefreshAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        older.SetResult();
        await olderRefresh.ConfigureAwait(true);

        TagItemViewModel only = Assert.Single(sut.Items);
        Assert.Equal("a", only.Slug);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task RefreshAsync_WhenTheQueryIsCancelled_KeepsTheCurrentItems()
    {
        ControllableStatistics service = new();
        using TagCloudViewModel sut = CreateViewModel(service);
        sut.ApplySnapshot([new TagStatistic("Bericht", "bericht", 4, FixedTime)]);
        service.Failure = new OperationCanceledException();

        await sut.RefreshAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = Assert.Single(sut.Items);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task RefreshAsync_OnInvalidOperationException_KeepsTheCurrentItems()
    {
        ControllableStatistics service = new();
        using TagCloudViewModel sut = CreateViewModel(service);
        sut.ApplySnapshot([new TagStatistic("Bericht", "bericht", 4, FixedTime)]);
        service.Failure = new InvalidOperationException("kaputter Zustand");

        await sut.RefreshAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = Assert.Single(sut.Items);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task RefreshAsync_OnDbException_KeepsTheCurrentItems()
    {
        ControllableStatistics service = new();
        using TagCloudViewModel sut = CreateViewModel(service);
        sut.ApplySnapshot([new TagStatistic("Bericht", "bericht", 4, FixedTime)]);
        service.Failure = new TestDbException("Datenbank belegt");

        await sut.RefreshAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = Assert.Single(sut.Items);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task RefreshAsync_OnEmptyResult_ResetsTheCountRangeToOne()
    {
        // Min und Max speisen die Schriftgrößen-Rechnung, und die verlangt mindestens 1; mit 0
        // würfe TagFontSizeCalculator beim Darstellen.
        ControllableStatistics service = new();
        using TagCloudViewModel sut = CreateViewModel(service);

        await sut.RefreshAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Empty(sut.Items);
        Assert.Equal(1, sut.MinCount);
        Assert.Equal(1, sut.MaxCount);
    }

    [Fact]
    public void ApplySnapshot_WithZeroCounts_ClampsTheLowerBoundToOne()
    {
        ControllableStatistics service = new();
        using TagCloudViewModel sut = CreateViewModel(service);

        sut.ApplySnapshot([new TagStatistic("Leer", "leer", 0, FixedTime)]);

        Assert.Equal(1, sut.MinCount);
        Assert.Equal(1, sut.MaxCount);
    }

    [Fact]
    public void ApplySnapshot_SortedByRecentUse_PutsTheNewestFirst()
    {
        ControllableStatistics service = new();
        using TagCloudViewModel sut = CreateViewModel(service);
        sut.Sort = TagCloudSortOption.RecentlyUsed;

        sut.ApplySnapshot(
        [
            new TagStatistic("Alt", "alt", 9, FixedTime),
            new TagStatistic("Neu", "neu", 1, FixedTime.AddDays(3)),
        ]);

        Assert.Equal("neu", sut.Items[0].Slug, StringComparer.Ordinal);
    }

    [Fact]
    public void ApplySnapshot_SortedByRecentUse_BreaksTiesBySlug()
    {
        ControllableStatistics service = new();
        using TagCloudViewModel sut = CreateViewModel(service);
        sut.Sort = TagCloudSortOption.RecentlyUsed;

        sut.ApplySnapshot(
        [
            new TagStatistic("Beta", "beta", 1, FixedTime),
            new TagStatistic("Alpha", "alpha", 1, FixedTime),
        ]);

        Assert.Equal("alpha", sut.Items[0].Slug, StringComparer.Ordinal);
    }

    [Fact]
    public void ApplySnapshot_SortedByFrequency_BreaksTiesBySlug()
    {
        ControllableStatistics service = new();
        using TagCloudViewModel sut = CreateViewModel(service);

        sut.ApplySnapshot(
        [
            new TagStatistic("Beta", "beta", 5, FixedTime),
            new TagStatistic("Alpha", "alpha", 5, FixedTime),
        ]);

        Assert.Equal("alpha", sut.Items[0].Slug, StringComparer.Ordinal);
    }

    [Fact]
    public void ApplySnapshot_WithoutSnapshot_Throws()
    {
        ControllableStatistics service = new();
        using TagCloudViewModel sut = CreateViewModel(service);

        _ = Assert.Throws<ArgumentNullException>(() => sut.ApplySnapshot(null!));
    }

    [Fact]
    public void Receive_WithoutMessage_Throws()
    {
        ControllableStatistics service = new();
        using TagCloudViewModel sut = CreateViewModel(service);

        _ = Assert.Throws<ArgumentNullException>(() => sut.Receive(null!));
    }

    [Fact]
    public void HandleTagClicked_WithoutItem_Throws()
    {
        ControllableStatistics service = new();
        using TagCloudViewModel sut = CreateViewModel(service);

        _ = Assert.Throws<ArgumentNullException>(() => sut.HandleTagClicked(null!, TagFilterMode.Add));
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        ControllableStatistics service = new();
        TagCloudViewModel sut = CreateViewModel(service);

        sut.Dispose();
        sut.Dispose();
    }

    private static TagCloudViewModel CreateViewModel(ITagStatisticsService service, TagCloudOptions? options = null) =>
        new(service,
            new StrongReferenceMessenger(),
            MicrosoftOptions.Create(options ?? new TagCloudOptions()),
            NullLogger<TagCloudViewModel>.Instance);

    private static TagStatistic Stat(string slug, int count) => new(slug, slug, count, FixedTime);

    /// <summary>Statistik-Dienst, dessen Ergebnis, Fehler und Zeitpunkt der Test vorgibt.</summary>
    private sealed class ControllableStatistics : ITagStatisticsService
    {
        private TaskCompletionSource? _gate;

        public IReadOnlyList<TagStatistic> Result { get; set; } = [];

        public Exception? Failure { get; set; }

        public int LastTopN { get; private set; }

        public CallSignal Calls { get; } = new();

        public int CallCount => Calls.Count;

        /// <summary>Lässt den nächsten Abruf hängen, bis <see cref="Release"/> gerufen wird.</summary>
        public TaskCompletionSource Block()
        {
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _gate = gate;
            return gate;
        }

        /// <summary>Lässt folgende Abrufe durch; ein schon hängender bleibt hängen.</summary>
        public void Unblock() => _gate = null;

        public void Release() => _gate?.TrySetResult();

        public async Task<IReadOnlyList<TagStatistic>> GetTopTagsAsync(int topN, CancellationToken cancellationToken)
        {
            LastTopN = topN;
            IReadOnlyList<TagStatistic> result = Result;
            Calls.Record();

            TaskCompletionSource? gate = _gate;
            if (gate is not null)
            {
                await gate.Task.ConfigureAwait(false);
            }
            if (Failure is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(Failure).Throw();
            }
            return result;
        }
    }
}
