using System.Data.Common;
using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.Messaging;
using MdExplorer.TagCloud.Models;
using MdExplorer.TagCloud.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;
using TagCloudOptions = MdExplorer.TagCloud.Options.TagCloudOptions;

namespace MdExplorer.TagCloud.Tests.ViewModels;

/// <summary>
/// Prüft den Ladeweg der Tag-Cloud: <see cref="TagCloudViewModel.RefreshAsync"/> mitsamt
/// Beschäftigt-Anzeige, Long-Tail-Umschaltung und Fehlerverhalten. Der Fehlerpfad ist der
/// wichtigste Teil: Bei einer Datenbank-Spitze muss die bereits angezeigte Wolke stehen
/// bleiben statt zu leeren — sonst verschwinden dem Nutzer beim Tippen die Tags.
/// </summary>
public sealed class TagCloudViewModelRefreshTests
{
    private static readonly DateTime FesteZeit = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RefreshAsync_OnResult_FillsItemsAndCountRange()
    {
        SteuerbareStatistik dienst = new();
        dienst.Ergebnis =
        [
            new TagStatistic("Bericht", "bericht", 9, FesteZeit),
            new TagStatistic("Notiz", "notiz", 3, FesteZeit),
        ];
        using TagCloudViewModel sut = Erzeuge(dienst);

        await sut.RefreshAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(2, sut.Items.Count);
        Assert.Equal("bericht", sut.Items[0].Slug, StringComparer.Ordinal);
        Assert.Equal(3, sut.MinCount);
        Assert.Equal(9, sut.MaxCount);
    }

    [Fact]
    public async Task RefreshAsync_OnCompletion_ClearsTheBusyFlag()
    {
        SteuerbareStatistik dienst = new();
        using TagCloudViewModel sut = Erzeuge(dienst);

        await sut.RefreshAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task RefreshAsync_WhileLoading_SetsTheBusyFlag()
    {
        // Ohne die Anzeige wirkt eine langsame Abfrage wie eine eingefrorene Oberfläche.
        SteuerbareStatistik dienst = new();
        dienst.Blockieren();
        using TagCloudViewModel sut = Erzeuge(dienst);

        Task laufend = sut.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.True(sut.IsBusy);
        dienst.Freigeben();
        await laufend.ConfigureAwait(true);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task RefreshAsync_ByDefault_RequestsTheStandardTopN()
    {
        SteuerbareStatistik dienst = new();
        using TagCloudViewModel sut = Erzeuge(dienst, new TagCloudOptions { TopN = 25, LongTailTopN = 700 });

        await sut.RefreshAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(25, dienst.LetztesTopN);
    }

    [Fact]
    public async Task RefreshAsync_WithExpandedLongTail_RequestsTheLargerTopN()
    {
        SteuerbareStatistik dienst = new();
        using TagCloudViewModel sut = Erzeuge(dienst, new TagCloudOptions { TopN = 25, LongTailTopN = 700 });
        sut.IsLongTailExpanded = true;

        await sut.RefreshAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(700, dienst.LetztesTopN);
        Assert.Equal(700, sut.EffectiveTopN);
    }

    [Fact]
    public async Task ExpandingTheLongTail_TriggersARefreshOnItsOwn()
    {
        // Das Umschalten ist die einzige Bedienhandlung, die selbst nachladen muss —
        // sonst zeigt die aufgeklappte Wolke weiter nur die Top-N.
        SteuerbareStatistik dienst = new();
        using TagCloudViewModel sut = Erzeuge(dienst);
        int vorher = dienst.Aufrufe;

        sut.IsLongTailExpanded = true;

        Assert.True(
            await WarteAufAsync(() => dienst.Aufrufe > vorher).ConfigureAwait(true),
            "Das Aufklappen hat keine Aktualisierung ausgelöst.");
    }

    [Fact]
    public async Task RefreshAsync_WhenTheQueryIsCancelled_KeepsTheCurrentItems()
    {
        SteuerbareStatistik dienst = new();
        using TagCloudViewModel sut = Erzeuge(dienst);
        sut.ApplySnapshot([new TagStatistic("Bericht", "bericht", 4, FesteZeit)]);
        dienst.Fehler = new OperationCanceledException();

        await sut.RefreshAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = Assert.Single(sut.Items);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task RefreshAsync_OnInvalidOperationException_KeepsTheCurrentItems()
    {
        SteuerbareStatistik dienst = new();
        using TagCloudViewModel sut = Erzeuge(dienst);
        sut.ApplySnapshot([new TagStatistic("Bericht", "bericht", 4, FesteZeit)]);
        dienst.Fehler = new InvalidOperationException("kaputter Zustand");

        await sut.RefreshAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = Assert.Single(sut.Items);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task RefreshAsync_OnDbException_KeepsTheCurrentItems()
    {
        SteuerbareStatistik dienst = new();
        using TagCloudViewModel sut = Erzeuge(dienst);
        sut.ApplySnapshot([new TagStatistic("Bericht", "bericht", 4, FesteZeit)]);
        dienst.Fehler = new TestDbException("Datenbank belegt");

        await sut.RefreshAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = Assert.Single(sut.Items);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task RefreshAsync_OnEmptyResult_ResetsTheCountRangeToOne()
    {
        // Min und Max speisen die Schriftgrößen-Rechnung; 0 wäre dort eine Division durch null.
        SteuerbareStatistik dienst = new();
        using TagCloudViewModel sut = Erzeuge(dienst);

        await sut.RefreshAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Empty(sut.Items);
        Assert.Equal(1, sut.MinCount);
        Assert.Equal(1, sut.MaxCount);
    }

    [Fact]
    public void ApplySnapshot_WithZeroCounts_ClampsTheLowerBoundToOne()
    {
        SteuerbareStatistik dienst = new();
        using TagCloudViewModel sut = Erzeuge(dienst);

        sut.ApplySnapshot([new TagStatistic("Leer", "leer", 0, FesteZeit)]);

        Assert.Equal(1, sut.MinCount);
        Assert.Equal(1, sut.MaxCount);
    }

    [Fact]
    public void ApplySnapshot_SortedByRecentUse_PutsTheNewestFirst()
    {
        SteuerbareStatistik dienst = new();
        using TagCloudViewModel sut = Erzeuge(dienst);
        sut.Sort = TagCloudSortOption.RecentlyUsed;

        sut.ApplySnapshot(
        [
            new TagStatistic("Alt", "alt", 9, FesteZeit),
            new TagStatistic("Neu", "neu", 1, FesteZeit.AddDays(3)),
        ]);

        Assert.Equal("neu", sut.Items[0].Slug, StringComparer.Ordinal);
    }

    [Fact]
    public void ApplySnapshot_SortedByRecentUse_BreaksTiesBySlug()
    {
        SteuerbareStatistik dienst = new();
        using TagCloudViewModel sut = Erzeuge(dienst);
        sut.Sort = TagCloudSortOption.RecentlyUsed;

        sut.ApplySnapshot(
        [
            new TagStatistic("Beta", "beta", 1, FesteZeit),
            new TagStatistic("Alpha", "alpha", 1, FesteZeit),
        ]);

        Assert.Equal("alpha", sut.Items[0].Slug, StringComparer.Ordinal);
    }

    [Fact]
    public void ApplySnapshot_SortedByFrequency_BreaksTiesBySlug()
    {
        SteuerbareStatistik dienst = new();
        using TagCloudViewModel sut = Erzeuge(dienst);

        sut.ApplySnapshot(
        [
            new TagStatistic("Beta", "beta", 5, FesteZeit),
            new TagStatistic("Alpha", "alpha", 5, FesteZeit),
        ]);

        Assert.Equal("alpha", sut.Items[0].Slug, StringComparer.Ordinal);
    }

    [Fact]
    public void ApplySnapshot_WithoutSnapshot_Throws()
    {
        SteuerbareStatistik dienst = new();
        using TagCloudViewModel sut = Erzeuge(dienst);

        _ = Assert.Throws<ArgumentNullException>(() => sut.ApplySnapshot(null!));
    }

    [Fact]
    public void Receive_WithoutMessage_Throws()
    {
        SteuerbareStatistik dienst = new();
        using TagCloudViewModel sut = Erzeuge(dienst);

        _ = Assert.Throws<ArgumentNullException>(() => sut.Receive(null!));
    }

    [Fact]
    public void HandleTagClicked_WithoutItem_Throws()
    {
        SteuerbareStatistik dienst = new();
        using TagCloudViewModel sut = Erzeuge(dienst);

        _ = Assert.Throws<ArgumentNullException>(() => sut.HandleTagClicked(null!, TagFilterMode.Add));
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        SteuerbareStatistik dienst = new();
        TagCloudViewModel sut = Erzeuge(dienst);

        sut.Dispose();
        sut.Dispose();
    }

    private static TagCloudViewModel Erzeuge(ITagStatisticsService dienst, TagCloudOptions? optionen = null) =>
        new(dienst,
            new StrongReferenceMessenger(),
            MicrosoftOptions.Create(optionen ?? new TagCloudOptions()),
            NullLogger<TagCloudViewModel>.Instance);

    /// <summary>Wartet kurz auf eine Bedingung, die eine nebenläufig gestartete Aufgabe erfüllt.</summary>
    private static async Task<bool> WarteAufAsync(Func<bool> bedingung)
    {
        DateTimeOffset ende = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < ende)
        {
            if (bedingung())
            {
                return true;
            }
            await Task.Delay(5).ConfigureAwait(false);
        }
        return bedingung();
    }

    /// <summary>Statistik-Dienst, dessen Ergebnis, Fehler und Zeitpunkt der Test vorgibt.</summary>
    private sealed class SteuerbareStatistik : ITagStatisticsService
    {
        private TaskCompletionSource? _sperre;
        private int _aufrufe;

        public IReadOnlyList<TagStatistic> Ergebnis { get; set; } = [];

        public Exception? Fehler { get; set; }

        public int LetztesTopN { get; private set; }

        public int Aufrufe => Volatile.Read(ref _aufrufe);

        /// <summary>Lässt den nächsten Abruf hängen, bis <see cref="Freigeben"/> gerufen wird.</summary>
        public void Blockieren() => _sperre = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Freigeben() => _sperre?.TrySetResult();

        public async Task<IReadOnlyList<TagStatistic>> GetTopTagsAsync(int topN, CancellationToken cancellationToken)
        {
            LetztesTopN = topN;
            _ = Interlocked.Increment(ref _aufrufe);

            TaskCompletionSource? sperre = _sperre;
            if (sperre is not null)
            {
                await sperre.Task.ConfigureAwait(false);
            }
            if (Fehler is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(Fehler).Throw();
            }
            return Ergebnis;
        }
    }

    /// <summary><see cref="DbException"/> ist abstrakt — für den Fehlerpfad braucht es eine eigene Ausprägung.</summary>
    private sealed class TestDbException : DbException
    {
        public TestDbException()
        {
        }

        public TestDbException(string message)
            : base(message)
        {
        }

        public TestDbException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
