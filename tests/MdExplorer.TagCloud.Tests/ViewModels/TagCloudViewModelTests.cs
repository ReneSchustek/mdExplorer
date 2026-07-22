using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.Messaging;
using MdExplorer.TagCloud.Models;
using MdExplorer.TagCloud.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;
using TagCloudOptions = MdExplorer.TagCloud.Options.TagCloudOptions;

namespace MdExplorer.TagCloud.Tests.ViewModels;

/// <summary>Unit-Tests des Tag-Cloud-ViewModels.</summary>
public sealed class TagCloudViewModelTests
{
    private static readonly DateTime FixedUtc = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);


    [Fact]
    public void TagCloudViewModel_OnTagClicked_PublishesAddFilterMessage()
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

        TagItemViewModel item = new(new TagStatistic("Tooling", "tooling", 7, FixedUtc));
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
    public void SortChange_OnAlphabetical_ResortsExistingSnapshot()
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

    private sealed class FakeStatisticsService : ITagStatisticsService
    {
        public Task<IReadOnlyList<TagStatistic>> GetTopTagsAsync(int topN, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TagStatistic>>(Array.Empty<TagStatistic>());
    }
}
