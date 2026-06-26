using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.Messaging;
using MdExplorer.TagCloud.Models;
using MdExplorer.TagCloud.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;
using TagCloudOptions = MdExplorer.TagCloud.Options.TagCloudOptions;

namespace MdExplorer.TagCloud.Tests.Services;

/// <summary>Unit-Tests des <see cref="TagCloudRefreshService"/>.</summary>
public sealed class TagCloudRefreshServiceTests
{
    private static readonly DateTime FixedUtc = new(2026, 6, 12, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PublishIfChangedAsync_OnFirstSnapshot_SendsTagsRefreshedMessage()
    {
        StrongReferenceMessenger messenger = new();
        FakeTagStatisticsService statsService = new();
        statsService.SetSnapshot(new TagStatistic("Docs", "docs", 3, FixedUtc));
        using TagCloudRefreshService sut = CreateService(messenger, statsService);

        TagsRefreshedMessage? received = null;
        messenger.Register<TagsRefreshedMessage>(this, (_, message) => received = message);

        await sut.PublishIfChangedAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.NotNull(received);
        _ = Assert.Single(received!.Snapshot);
        Assert.Equal("docs", received.Snapshot[0].Slug);

        messenger.UnregisterAll(this);
    }

    [Fact]
    public async Task PublishIfChangedAsync_OnUnchangedSnapshot_DoesNotResend()
    {
        StrongReferenceMessenger messenger = new();
        FakeTagStatisticsService statsService = new();
        statsService.SetSnapshot(new TagStatistic("Docs", "docs", 3, FixedUtc));
        using TagCloudRefreshService sut = CreateService(messenger, statsService);

        int received = 0;
        messenger.Register<TagsRefreshedMessage>(this, (_, _) => received++);

        await sut.PublishIfChangedAsync(CancellationToken.None).ConfigureAwait(true);
        await sut.PublishIfChangedAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, received);

        messenger.UnregisterAll(this);
    }

    [Fact]
    public async Task PublishIfChangedAsync_OnCountChange_Resends()
    {
        StrongReferenceMessenger messenger = new();
        FakeTagStatisticsService statsService = new();
        statsService.SetSnapshot(new TagStatistic("Docs", "docs", 3, FixedUtc));
        using TagCloudRefreshService sut = CreateService(messenger, statsService);

        int received = 0;
        TagsRefreshedMessage? last = null;
        messenger.Register<TagsRefreshedMessage>(this, (_, message) =>
        {
            received++;
            last = message;
        });

        await sut.PublishIfChangedAsync(CancellationToken.None).ConfigureAwait(true);
        statsService.SetSnapshot(new TagStatistic("Docs", "docs", 4, FixedUtc));
        await sut.PublishIfChangedAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, received);
        Assert.NotNull(last);
        Assert.Equal(4, last!.Snapshot[0].Count);

        messenger.UnregisterAll(this);
    }

    [Fact]
    public async Task PublishIfChangedAsync_OnLastUsedChange_Resends()
    {
        StrongReferenceMessenger messenger = new();
        FakeTagStatisticsService statsService = new();
        statsService.SetSnapshot(new TagStatistic("Docs", "docs", 3, FixedUtc));
        using TagCloudRefreshService sut = CreateService(messenger, statsService);

        int received = 0;
        TagsRefreshedMessage? last = null;
        messenger.Register<TagsRefreshedMessage>(this, (_, message) =>
        {
            received++;
            last = message;
        });

        await sut.PublishIfChangedAsync(CancellationToken.None).ConfigureAwait(true);
        DateTime newer = FixedUtc.AddMinutes(5);
        statsService.SetSnapshot(new TagStatistic("Docs", "docs", 3, newer));
        await sut.PublishIfChangedAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, received);
        Assert.NotNull(last);
        Assert.Equal(newer, last!.Snapshot[0].LastUsedUtc);

        messenger.UnregisterAll(this);
    }

    [Fact]
    public async Task PublishIfChangedAsync_OnArgumentException_LogsAndContinues()
    {
        StrongReferenceMessenger messenger = new();
        FakeTagStatisticsService statsService = new();
        statsService.SetThrow(new ArgumentException("korrupter Tag-Wert"));
        using TagCloudRefreshService sut = CreateService(messenger, statsService);

        int received = 0;
        messenger.Register<TagsRefreshedMessage>(this, (_, _) => received++);

        // ArgumentException aus dem Tag-Statistik-Pfad darf nicht durchschlagen.
        await sut.PublishIfChangedAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(0, received);

        // Naechster Snapshot wird normal published.
        statsService.SetSnapshot(new TagStatistic("Docs", "docs", 3, FixedUtc));
        await sut.PublishIfChangedAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(1, received);

        messenger.UnregisterAll(this);
    }

    [Fact]
    public async Task PublishIfChangedAsync_OnInvalidOperationException_LogsAndContinues()
    {
        StrongReferenceMessenger messenger = new();
        FakeTagStatisticsService statsService = new();
        statsService.SetThrow(new InvalidOperationException("query failed"));
        using TagCloudRefreshService sut = CreateService(messenger, statsService);

        int received = 0;
        messenger.Register<TagsRefreshedMessage>(this, (_, _) => received++);

        await sut.PublishIfChangedAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(0, received);

        // Wenn die naechste Iteration einen gueltigen Snapshot liefert, muss er publiziert werden —
        // _lastSignature darf vom Fehler nicht beeinflusst worden sein.
        statsService.SetSnapshot(new TagStatistic("Docs", "docs", 3, FixedUtc));
        await sut.PublishIfChangedAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, received);

        messenger.UnregisterAll(this);
    }

    [Fact]
    public async Task ExecuteAsync_OnPeriodicTick_TriggersRefresh()
    {
        StrongReferenceMessenger messenger = new();
        FakeTagStatisticsService statsService = new();
        statsService.SetSnapshot(new TagStatistic("Docs", "docs", 1, FixedUtc));
        FakeTimeProvider timeProvider = new();
        TagCloudOptions options = new() { RefreshIntervalSeconds = 1 };

        ServiceCollection services = new();
        _ = services.AddSingleton<ITagStatisticsService>(statsService);
        using ServiceProvider provider = services.BuildServiceProvider();

        using TagCloudRefreshService sut = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            messenger,
            timeProvider,
            MicrosoftOptions.Create(options),
            NullLogger<TagCloudRefreshService>.Instance);

        await sut.StartAsync(CancellationToken.None).ConfigureAwait(true);
        try
        {
            await WaitForAsync(() => statsService.CallCount >= 1, TimeSpan.FromSeconds(5)).ConfigureAwait(true);

            statsService.SetSnapshot(new TagStatistic("Docs", "docs", 2, FixedUtc));
            timeProvider.Advance(TimeSpan.FromSeconds(1));

            await WaitForAsync(() => statsService.CallCount >= 2, TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None).ConfigureAwait(true);
        }

        Assert.Equal(2, statsService.CallCount);
    }

    private static TagCloudRefreshService CreateService(
        IMessenger messenger,
        FakeTagStatisticsService statsService,
        int refreshIntervalSeconds = 5)
    {
        ServiceCollection services = new();
        _ = services.AddSingleton<ITagStatisticsService>(statsService);
        ServiceProvider provider = services.BuildServiceProvider();
        TagCloudOptions options = new() { RefreshIntervalSeconds = refreshIntervalSeconds };
        return new TagCloudRefreshService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            messenger,
            TimeProvider.System,
            MicrosoftOptions.Create(options),
            NullLogger<TagCloudRefreshService>.Instance);
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(10).ConfigureAwait(true);
        }
        if (!condition())
        {
            throw new TimeoutException($"Bedingung nicht innerhalb von {timeout.TotalSeconds:F1}s erfuellt.");
        }
    }

    private sealed class FakeTagStatisticsService : ITagStatisticsService
    {
        private readonly object _gate = new();
        private IReadOnlyList<TagStatistic> _snapshot = [];
        private Exception? _throwOnNext;
        private int _callCount;

        public int CallCount
        {
            get { lock (_gate) { return _callCount; } }
        }

        public void SetSnapshot(params TagStatistic[] tags)
        {
            lock (_gate)
            {
                _snapshot = tags;
                _throwOnNext = null;
            }
        }

        public void SetThrow(Exception exception)
        {
            lock (_gate)
            {
                _throwOnNext = exception;
            }
        }

        public Task<IReadOnlyList<TagStatistic>> GetTopTagsAsync(int topN, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _callCount++;
                if (_throwOnNext is not null)
                {
                    Exception toThrow = _throwOnNext;
                    return Task.FromException<IReadOnlyList<TagStatistic>>(toThrow);
                }
                return Task.FromResult(_snapshot);
            }
        }
    }
}
