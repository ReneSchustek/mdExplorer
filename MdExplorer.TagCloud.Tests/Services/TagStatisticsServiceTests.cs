using System.Diagnostics;
using MdExplorer.TagCloud.Models;
using MdExplorer.TagCloud.Services;

namespace MdExplorer.TagCloud.Tests.Services;

/// <summary>
/// Integrationstests des <see cref="TagStatisticsService"/> gegen eine echte SQLite-Datei.
/// Verifiziert Korrektheit der GROUP-BY-Aggregation sowie das Performance-Budget
/// (Median ≤ 80 ms / p95 ≤ 200 ms bei 10.000 Dokumenten).
/// Parallelisierung ist auf Assembly-Ebene deaktiviert (siehe <c>AssemblyInfo.cs</c>),
/// damit Performance-Messungen unter SQLite-Datei-I/O stabil bleiben.
/// </summary>
public sealed class TagStatisticsServiceTests
{
    [Fact]
    public async Task TagStatisticsService_OnEmptyDb_ReturnsEmpty()
    {
        TagStatisticsTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            IReadOnlyList<TagStatistic> result = await harness.Service
                .GetTopTagsAsync(topN: 50, CancellationToken.None)
                .ConfigureAwait(true);

            Assert.Empty(result);
        }
    }

    [Fact]
    public async Task TagStatisticsService_OnGroupByTag_ReturnsCorrectCounts()
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
                .GetTopTagsAsync(topN: 10, CancellationToken.None)
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
    public async Task TagStatisticsService_OnTopN_LimitsResults()
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
                .GetTopTagsAsync(topN: 5, CancellationToken.None)
                .ConfigureAwait(true);

            Assert.Equal(5, result.Count);
        }
    }

    [Fact]
    public async Task TagStatisticsService_OnLargeDataset_RespondsWithinP95Budget()
    {
        // Budget-Strategie: Der Test soll nur eine *Regression* der Query-Pipeline
        // anschlagen (>3× langsamer als heute), nicht externe Effekte (Antivirus-Scan auf der
        // tempo SQLite-Datei, Build-Last, JIT-Cold-Spikes) fälschlich als Bug reporten.
        //
        // Realistische Messung (Release, lokal, im Leerlauf): Median ~30 ms, p95 ~50 ms.
        // CI-Hosts und Antivirus-Spikes treiben Einzelmessungen sporadisch bis ~150 ms.
        //
        // Median-Budget 80 ms: Wenn der typische Lauf das überschreitet, ist die Pipeline
        // tatsächlich kaputt — kein temporäres Spike-Tail kann den Median lange halten.
        // p95-Budget 200 ms: bewusst großzügig, damit der Test in 10 aufeinanderfolgenden
        // Läufen unter Build-Last grün bleibt.
        const int p95BudgetMs = 200;
        const int medianBudgetMs = 80;

        TagStatisticsTestHarness harness = new();
        await using (harness.ConfigureAwait(true))
        {
            DateTime baseTime = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);
            await harness.SeedLargeDatasetAsync(documentCount: 10_000, distinctTagCount: 200, baseTime).ConfigureAwait(true);

            // Warm-Up — fünf Vor-Iterationen decken EF-Metadaten, SQLite-Page-Cache, JIT und
            // GC-Stabilisierung ab; sollte das Tail-Spike-Risiko der ersten paar gemessenen
            // Iterationen praktisch eliminieren.
            for (int warm = 0; warm < 5; warm++)
            {
                _ = await harness.Service.GetTopTagsAsync(topN: 50, CancellationToken.None).ConfigureAwait(true);
            }

            const int iterations = 60;
            long[] durationsMs = new long[iterations];
            for (int index = 0; index < iterations; index++)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                _ = await harness.Service.GetTopTagsAsync(topN: 50, CancellationToken.None).ConfigureAwait(true);
                stopwatch.Stop();
                durationsMs[index] = stopwatch.ElapsedMilliseconds;
            }

            Array.Sort(durationsMs);
            long median = durationsMs[iterations / 2];
            long p95 = durationsMs[(int)Math.Floor(iterations * 0.95) - 1];

            Assert.True(median <= medianBudgetMs, $"Median = {median}ms (Budget {medianBudgetMs}ms). Roh-Messungen ms: [{string.Join(", ", durationsMs)}].");
            Assert.True(p95 <= p95BudgetMs, $"p95 = {p95}ms (Budget {p95BudgetMs}ms). Roh-Messungen ms: [{string.Join(", ", durationsMs)}].");
        }
    }
}
