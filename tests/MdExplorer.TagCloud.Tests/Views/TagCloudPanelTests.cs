using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.Messaging;
using MdExplorer.TagCloud.Models;
using MdExplorer.TagCloud.ViewModels;
using MdExplorer.TagCloud.Views;
using Microsoft.Extensions.Logging.Abstractions;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;
using TagCloudOptions = MdExplorer.TagCloud.Options.TagCloudOptions;

namespace MdExplorer.TagCloud.Tests.Views;

/// <summary>
/// Die Wolke zeigt häufige Schlagworte größer als seltene. Stehen alle in einer Größe, ist sie
/// nur eine Liste, und die Häufigkeit ist erst im Tooltip zu erfahren.
/// </summary>
public sealed class TagCloudPanelTests
{
    private static readonly DateTime FixedUtc = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ThePanel_ShowsAFrequentTagLargerThanARareOne()
    {
        StaRunner.Run(() =>
        {
            TagCloudOptions options = new() { MinFontSize = 10, MaxFontSize = 26 };
            using TagCloudViewModel viewModel = new(
                new EmptyStatisticsService(),
                new StrongReferenceMessenger(),
                MicrosoftOptions.Create(options),
                NullLogger<TagCloudViewModel>.Instance);
            viewModel.Receive(new TagsRefreshedMessage(
            [
                new TagStatistic("Docs", "docs", 100, FixedUtc),
                new TagStatistic("Rest", "rest", 1, FixedUtc),
            ]));

            TagCloudPanel panel = new() { DataContext = viewModel };
            panel.Measure(new Size(400, 400));
            panel.Arrange(new Rect(0, 0, 400, 400));
            panel.UpdateLayout();

            List<TextBlock> blocks = [];
            CollectTextBlocks(panel, blocks);
            double frequent = blocks.Single(block => block.Text == "Docs").FontSize;
            double rare = blocks.Single(block => block.Text == "Rest").FontSize;

            Assert.Equal(26, frequent);
            Assert.Equal(10, rare);
        });
    }

    private static void CollectTextBlocks(DependencyObject parent, List<TextBlock> blocks)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is TextBlock block)
            {
                blocks.Add(block);
            }
            CollectTextBlocks(child, blocks);
        }
    }

    /// <summary>Statistik ohne Schlagworte; der Test füttert das ViewModel über eine Nachricht.</summary>
    private sealed class EmptyStatisticsService : ITagStatisticsService
    {
        public Task<IReadOnlyList<TagStatistic>> GetTopTagsAsync(int topN, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TagStatistic>>([]);
    }
}
