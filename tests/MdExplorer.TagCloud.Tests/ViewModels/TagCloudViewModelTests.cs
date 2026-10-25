using System.Globalization;
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
/// Das ViewModel der Schlagwortwolke übernimmt einen neuen Stand samt Spanne der Anzahlen, sortiert
/// um, meldet einen Klick als Filter-Nachricht und meldet sich beim Freigeben vom Messenger ab.
/// Bliebe es angemeldet, verarbeitete ein freigegebenes ViewModel weiter Nachrichten, bis die
/// Speicherbereinigung es einsammelt.
/// </summary>
public sealed class TagCloudViewModelTests
{
    private static readonly DateTime FixedUtc = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);


    [Fact]
    public void HandleTagClicked_InAddMode_PublishesAddFilterMessage()
    {
        StrongReferenceMessenger messenger = new();
        FakeStatisticsService statisticsService = new();
        TagCloudOptions options = new();

        using TagCloudViewModel viewModel = new(
            statisticsService,
            messenger,
            MicrosoftOptions.Create(options),
            NullLogger<TagCloudViewModel>.Instance);

        TagClickedMessage? received = null;
        messenger.Register<TagClickedMessage>(this, (recipient, message) => received = message);

        TagItemViewModel item = new(new TagStatistic("Tooling", "tooling", 7, FixedUtc), fontSize: 14);
        viewModel.HandleTagClicked(item, TagFilterMode.Add);

        Assert.NotNull(received);
        Assert.Equal("tooling", received!.Slug);
        Assert.Equal("Tooling", received.DisplayName);
        Assert.Equal(TagFilterMode.Add, received.Mode);

        messenger.UnregisterAll(this);
    }

    [Fact]
    public void Receive_OnSnapshot_PopulatesItemsAndCountRange()
    {
        StrongReferenceMessenger messenger = new();
        FakeStatisticsService statisticsService = new();
        TagCloudOptions options = new();

        using TagCloudViewModel viewModel = new(
            statisticsService,
            messenger,
            MicrosoftOptions.Create(options),
            NullLogger<TagCloudViewModel>.Instance);

        DateTime now = new(2026, 6, 8, 12, 0, 0, DateTimeKind.Utc);
        TagStatistic[] snapshot =
        [
            new("Build", "build", 4, now),
            new("Docs", "docs", 12, now.AddMinutes(-30)),
            new("Tests", "tests", 7, now.AddMinutes(-15)),
        ];

        viewModel.Receive(new TagsRefreshedMessage(snapshot));

        Assert.Equal(3, viewModel.Items.Count);
        Assert.Equal("docs", viewModel.Items[0].Slug);
        Assert.Equal(4, viewModel.MinCount);
        Assert.Equal(12, viewModel.MaxCount);
    }

    [Fact]
    public void Receive_WhileTheLongTailIsCollapsed_KeepsOnlyTheTopTags()
    {
        // Der Hintergrunddienst lädt die lange Liste, damit das Aufklappen nichts nachladen muss.
        // Die zugeklappte Wolke zeigt trotzdem nur die häufigsten.
        StrongReferenceMessenger messenger = new();
        TagCloudOptions options = new() { TopN = 2, LongTailTopN = 10 };

        using TagCloudViewModel viewModel = new(
            new FakeStatisticsService(),
            messenger,
            MicrosoftOptions.Create(options),
            NullLogger<TagCloudViewModel>.Instance);

        TagStatistic[] snapshot =
        [
            new("Build", "build", 4, FixedUtc),
            new("Docs", "docs", 12, FixedUtc),
            new("Tests", "tests", 7, FixedUtc),
            new("Rest", "rest", 1, FixedUtc),
        ];

        viewModel.Receive(new TagsRefreshedMessage(snapshot));

        Assert.Equal(["docs", "tests"], viewModel.Items.Select(item => item.Slug));
    }

    [Fact]
    public void Sort_ChangedToAlphabetical_ResortsTheExistingSnapshot()
    {
        StrongReferenceMessenger messenger = new();
        FakeStatisticsService statisticsService = new();
        TagCloudOptions options = new();

        using TagCloudViewModel viewModel = new(
            statisticsService,
            messenger,
            MicrosoftOptions.Create(options),
            NullLogger<TagCloudViewModel>.Instance);

        DateTime now = FixedUtc;
        TagStatistic[] snapshot =
        [
            new("Zebra", "zebra", 1, now),
            new("Alpha", "alpha", 99, now),
            new("Mango", "mango", 50, now),
        ];
        viewModel.Receive(new TagsRefreshedMessage(snapshot));

        viewModel.Sort = TagCloudSortOption.Alphabetical;

        Assert.Equal("alpha", viewModel.Items[0].Slug);
        Assert.Equal("mango", viewModel.Items[1].Slug);
        Assert.Equal("zebra", viewModel.Items[2].Slug);
    }

    [Fact]
    public void Sort_ChangedToAlphabetical_PutsUmlautsWhereGermanReadersExpectThem()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            using TagCloudViewModel viewModel = new(
                new FakeStatisticsService(),
                new StrongReferenceMessenger(),
                MicrosoftOptions.Create(new TagCloudOptions()),
                NullLogger<TagCloudViewModel>.Instance);
            viewModel.Receive(new TagsRefreshedMessage(
            [
                new("Zebra", "zebra", 1, FixedUtc),
                new("Äpfel", "äpfel", 1, FixedUtc),
                new("Birne", "birne", 1, FixedUtc),
            ]));

            viewModel.Sort = TagCloudSortOption.Alphabetical;

            Assert.Equal(["äpfel", "birne", "zebra"], viewModel.Items.Select(item => item.Slug));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Receive_OnSnapshot_ReplacesTheListOnceWithPrecomputedFontSizes()
    {
        TagCloudOptions options = new();
        using TagCloudViewModel viewModel = new(
            new FakeStatisticsService(),
            new StrongReferenceMessenger(),
            MicrosoftOptions.Create(options),
            NullLogger<TagCloudViewModel>.Instance);
        int itemsChanges = 0;
        viewModel.PropertyChanged += (_, args) => itemsChanges += args.PropertyName == nameof(TagCloudViewModel.Items) ? 1 : 0;

        viewModel.Receive(new TagsRefreshedMessage(
        [
            new("Selten", "selten", 1, FixedUtc),
            new("Oft", "oft", 50, FixedUtc),
        ]));

        Assert.Equal(1, itemsChanges);
        Assert.Equal(options.MaxFontSize, viewModel.Items.Single(item => item.Slug == "oft").FontSize);
        Assert.Equal(options.MinFontSize, viewModel.Items.Single(item => item.Slug == "selten").FontSize);
    }

    [Fact]
    public void Dispose_OnCall_UnregistersMessengerSubscriptions()
    {
        StrongReferenceMessenger messenger = new();
        FakeStatisticsService statisticsService = new();
        TagCloudOptions options = new();

        TagCloudViewModel viewModel = new(
            statisticsService,
            messenger,
            MicrosoftOptions.Create(options),
            NullLogger<TagCloudViewModel>.Instance);

        Assert.True(messenger.IsRegistered<TagsRefreshedMessage>(viewModel));
        viewModel.Dispose();
        Assert.False(messenger.IsRegistered<TagsRefreshedMessage>(viewModel));
    }

    /// <summary>Statistik ohne Schlagworte; die Tests füttern das ViewModel über Nachrichten.</summary>
    private sealed class FakeStatisticsService : ITagStatisticsService
    {
        public Task<IReadOnlyList<TagStatistic>> GetTopTagsAsync(int topN, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TagStatistic>>(Array.Empty<TagStatistic>());
    }
}
