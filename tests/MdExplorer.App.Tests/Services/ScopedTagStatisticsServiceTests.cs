using MdExplorer.App.Services;
using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Die Tag-Cloud fragt über einen Dienst, der je Abfrage einen eigenen Scope öffnet. Teilten
/// sich zwei Abfragen den Dienst dahinter, teilten sie sich auch dessen DbContext.
/// </summary>
public sealed class ScopedTagStatisticsServiceTests
{
    private static readonly DateTime LastUsed = new(2026, 10, 25, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetTopTagsAsync_TwoCalls_UseSeparateScopedInstances()
    {
        using ServiceProvider provider = BuildProvider();
        ScopedTagStatisticsService sut = new(provider.GetRequiredService<IServiceScopeFactory>());

        IReadOnlyList<TagStatistic> first = await sut.GetTopTagsAsync(5, TestContext.Current.CancellationToken);
        IReadOnlyList<TagStatistic> second = await sut.GetTopTagsAsync(5, TestContext.Current.CancellationToken);

        Assert.NotEqual(Assert.Single(first).Name, Assert.Single(second).Name);
    }

    [Fact]
    public async Task GetTopTagsAsync_AfterTheCall_DisposesTheScopedInstance()
    {
        using ServiceProvider provider = BuildProvider();
        ScopedTagStatisticsService sut = new(provider.GetRequiredService<IServiceScopeFactory>());

        _ = await sut.GetTopTagsAsync(3, TestContext.Current.CancellationToken);

        Assert.Equal(1, CountingStatistics.DisposedCount(provider));
    }

    [Fact]
    public async Task GetTopTagsAsync_PassesTopNThrough()
    {
        using ServiceProvider provider = BuildProvider();
        ScopedTagStatisticsService sut = new(provider.GetRequiredService<IServiceScopeFactory>());

        IReadOnlyList<TagStatistic> result = await sut.GetTopTagsAsync(7, TestContext.Current.CancellationToken);

        Assert.Equal(7, Assert.Single(result).Count);
    }

    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        _ = services.AddSingleton(_ => new CountingStatistics.Registry());
        _ = services.AddScoped<ITagStatisticsService>(sp => new CountingStatistics(sp.GetRequiredService<CountingStatistics.Registry>()));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Liefert je Instanz einen eigenen Namen und zählt, wie viele freigegeben wurden.</summary>
    private sealed class CountingStatistics(CountingStatistics.Registry registry) : ITagStatisticsService, IDisposable
    {
        private readonly int _instanceNumber = registry.NextInstanceNumber();

        public static int DisposedCount(IServiceProvider provider)
            => provider.GetRequiredService<Registry>().Disposed;

        public Task<IReadOnlyList<TagStatistic>> GetTopTagsAsync(int topN, CancellationToken cancellationToken)
        {
            TagStatistic statistic = new("instance-" + _instanceNumber, "instance", topN, LastUsed);
            return Task.FromResult<IReadOnlyList<TagStatistic>>([statistic]);
        }

        public void Dispose() => registry.Disposed++;

        /// <summary>Gemeinsamer Zähler über alle Instanzen eines Containers.</summary>
        internal sealed class Registry
        {
            private int _created;

            public int Disposed { get; set; }

            public int NextInstanceNumber() => ++_created;
        }
    }
}
