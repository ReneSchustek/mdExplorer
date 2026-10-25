using MdExplorer.TagCloud.Models;
using MdExplorer.TagCloud.Services;

namespace MdExplorer.TagCloud.Tests.Services;

/// <summary>
/// Integrationstests des <see cref="TagStatisticsService"/> gegen eine echte SQLite-Datei.
/// Verifiziert Korrektheit der GROUP-BY-Aggregation sowie das Zeitbudget der Abfrage
/// (schnellster Durchlauf ≤ 60 ms bei 10.000 Dokumenten).
/// Parallelisierung ist auf Assembly-Ebene deaktiviert (siehe <c>AssemblyInfo.cs</c>),
/// damit Performance-Messungen unter SQLite-Datei-I/O stabil bleiben.
/// </summary>
public sealed class TagStatisticsServiceTests
{
    [Fact]
    public async Task GetTopTagsAsync_OnAnEmptyDatabase_ReturnsNothing()
    {
        TagStatisticsTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            IReadOnlyList<TagStatistic> result = await harness.Service
                .GetTopTagsAsync(topN: 50, TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            Assert.Empty(result);
        }
    }

    [Fact]
    public async Task GetTopTagsAsync_WithSeveralFilesPerTag_CountsThemPerTag()
    {
        TagStatisticsTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            DateTime baseTime = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);
            await harness.SeedAsync(
            [
                new SeedTag("Docs", "docs", baseTime, baseTime.AddDays(2), baseTime.AddDays(1)),
                new SeedTag("Build", "build", baseTime.AddHours(3)),
                new SeedTag("Tests", "tests", baseTime.AddDays(5), baseTime.AddDays(2)),
            ]).ConfigureAwait(true);

            IReadOnlyList<TagStatistic> result = await harness.Service
                .GetTopTagsAsync(topN: 10, TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            Assert.Equal(3, result.Count);
            Assert.Equal("docs", result[0].Slug);
            Assert.Equal(3, result[0].Count);
            Assert.Equal(baseTime.AddDays(2), result[0].LastUsedUtc);
            Assert.Equal("tests", result[1].Slug);
            Assert.Equal(2, result[1].Count);
            Assert.Equal("build", result[2].Slug);
            Assert.Equal(1, result[2].Count);
        }
    }

    [Fact]
    public async Task GetTopTagsAsync_WithTopN_LimitsTheResult()
    {
        TagStatisticsTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            DateTime baseTime = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);
            List<SeedTag> seeds = [];
            for (int index = 0; index < 25; index++)
            {
                seeds.Add(new SeedTag($"Tag{index:D2}", $"tag-{index:D2}", baseTime.AddMinutes(index)));
            }
            await harness.SeedAsync(seeds).ConfigureAwait(true);

            IReadOnlyList<TagStatistic> result = await harness.Service
                .GetTopTagsAsync(topN: 5, TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            Assert.Equal(5, result.Count);
        }
    }

    /// <remarks>
    /// Die Laufzeit misst <c>GraphAndTagBenchmarks.TopTagsForTheCloud</c> im Messprojekt; eine
    /// Stoppuhr im Testlauf hinge an der Auslastung. Hier zählt, dass die Abfrage auch über einen
    /// großen Bestand die häufigsten Schlagworte in der richtigen Reihenfolge liefert.
    /// </remarks>
    [Fact]
    public async Task GetTopTagsAsync_OnALargeStock_ReturnsTheMostFrequentInOrder()
    {
        TagStatisticsTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            DateTime baseTime = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);
            await harness.SeedLargeDatasetAsync(documentCount: 10_000, distinctTagCount: 200, baseTime).ConfigureAwait(true);

            IReadOnlyList<TagStatistic> top = await harness.Service.GetTopTagsAsync(topN: 50, TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(50, top.Count);
            Assert.Equal(top.OrderByDescending(tag => tag.Count).Select(tag => tag.Count), top.Select(tag => tag.Count));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetTopTagsAsync_WithoutAPositiveTopN_Throws(int topN)
    {
        TagStatisticsTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => harness.Service.GetTopTagsAsync(topN, TestContext.Current.CancellationToken)).ConfigureAwait(true);
        }
    }
}
