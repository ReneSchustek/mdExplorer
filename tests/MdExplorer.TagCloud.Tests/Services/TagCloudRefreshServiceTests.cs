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
using MdExplorer.TagCloud.Tests.Fakes;

namespace MdExplorer.TagCloud.Tests.Services;

/// <summary>
/// Der <see cref="TagCloudRefreshService"/> meldet einen neuen Stand der Schlagwortwolke nur, wenn
/// sich Anzahl oder letzte Verwendung geändert hat, und übersteht einen Fehler der Statistik, ohne
/// den nächsten gültigen Stand zu verschlucken.
/// </summary>
public sealed class TagCloudRefreshServiceTests : IDisposable
{
    // Jeder Dienst bekommt einen eigenen Anbieter; freigegeben wird nach dem Test.
    private readonly List<ServiceProvider> _providers = [];

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);
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

        await sut.PublishIfChangedAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

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

        await sut.PublishIfChangedAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        await sut.PublishIfChangedAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

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

        await sut.PublishIfChangedAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        statsService.SetSnapshot(new TagStatistic("Docs", "docs", 4, FixedUtc));
        await sut.PublishIfChangedAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

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

        await sut.PublishIfChangedAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        DateTime newer = FixedUtc.AddMinutes(5);
        statsService.SetSnapshot(new TagStatistic("Docs", "docs", 3, newer));
        await sut.PublishIfChangedAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

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
        await sut.PublishIfChangedAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(0, received);

        // Der nächste gültige Stand wird normal gemeldet.
        statsService.SetSnapshot(new TagStatistic("Docs", "docs", 3, FixedUtc));
        await sut.PublishIfChangedAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
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

        await sut.PublishIfChangedAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(0, received);

        // Wenn die nächste Iteration einen gültigen Snapshot liefert, muss er publiziert werden;
        // _lastSignature darf vom Fehler nicht beeinflusst worden sein.
        statsService.SetSnapshot(new TagStatistic("Docs", "docs", 3, FixedUtc));
        await sut.PublishIfChangedAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

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
            new MdExplorer.Core.Diagnostics.BackgroundServiceStatus(),
            NullLogger<TagCloudRefreshService>.Instance);

        await sut.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        try
        {
            Assert.True(await statsService.Calls.WaitForAsync(1, Patience).ConfigureAwait(true), "Kein erster Abruf.");

            statsService.SetSnapshot(new TagStatistic("Docs", "docs", 2, FixedUtc));
            timeProvider.Advance(TimeSpan.FromSeconds(1));

            Assert.True(await statsService.Calls.WaitForAsync(2, Patience).ConfigureAwait(true), "Kein Abruf nach dem Takt.");
        }
        finally
        {
            await sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        }

        Assert.Equal(2, statsService.CallCount);
    }

    public void Dispose()
    {
        foreach (ServiceProvider provider in _providers)
        {
            provider.Dispose();
        }
    }

    private TagCloudRefreshService CreateService(
        IMessenger messenger,
        FakeTagStatisticsService statsService,
        int refreshIntervalSeconds = 5)
    {
        ServiceCollection services = new();
        _ = services.AddSingleton<ITagStatisticsService>(statsService);
        ServiceProvider provider = services.BuildServiceProvider();
        _providers.Add(provider);
        TagCloudOptions options = new() { RefreshIntervalSeconds = refreshIntervalSeconds };
        return new TagCloudRefreshService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            messenger,
            TimeProvider.System,
            MicrosoftOptions.Create(options),
            new MdExplorer.Core.Diagnostics.BackgroundServiceStatus(),
            NullLogger<TagCloudRefreshService>.Instance);
    }

    /// <summary>
    /// Statistik mit vorgegebenem Stand, die mitzählt. Eine mit <c>SetThrow</c> gesetzte Ausnahme
    /// wirft sie bei jedem Aufruf, bis <c>SetSnapshot</c> wieder einen Stand vorgibt.
    /// </summary>
    private sealed class FakeTagStatisticsService : ITagStatisticsService
    {
        private readonly object _gate = new();
        private IReadOnlyList<TagStatistic> _snapshot = [];
        private Exception? _throwOnNext;

        public CallSignal Calls { get; } = new();

        public int CallCount => Calls.Count;

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
            Calls.Record();
            lock (_gate)
            {
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
