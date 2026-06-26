using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.Messaging;
using MdExplorer.App.Services;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Update.Abstractions;
using MdExplorer.Update.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.Services;

/// <summary>Tests für die Orchestrierung des <see cref="UpdateCheckBackgroundService"/> (RunOnce).</summary>
public sealed class UpdateCheckBackgroundServiceTests : IDisposable
{
    private static readonly SemanticVersion Current = new(0, 9, 0);
    private static readonly SemanticVersion Newer = new(1, 0, 0);
    private static readonly Uri ReleaseUrl = new("https://github.com/ReneSchustek/mdExplorer/releases/latest");

    private readonly List<IDisposable> _disposables = [];

    [Fact]
    public async Task RunOnce_WhenUpdateAvailable_PublishesMessage()
    {
        FakeUpdateChecker checker = new(UpdateCheckResult.Available(Current, Newer, ReleaseUrl));
        StrongReferenceMessenger messenger = new();
        UpdateAvailableMessage? received = null;
        messenger.Register<UpdateAvailableMessage>(this, (_, message) => received = message);
        using UpdateCheckBackgroundService service = CreateService(checker, messenger, updatesEnabled: true);

        await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, checker.CallCount);
        Assert.NotNull(received);
        Assert.Equal("1.0.0", received!.Version);
        Assert.Equal(ReleaseUrl, received.ReleaseUrl);
    }

    [Fact]
    public async Task RunOnce_WhenDisabled_DoesNotCheckOrPublish()
    {
        FakeUpdateChecker checker = new(UpdateCheckResult.Available(Current, Newer, ReleaseUrl));
        StrongReferenceMessenger messenger = new();
        bool received = false;
        messenger.Register<UpdateAvailableMessage>(this, (_, _) => received = true);
        using UpdateCheckBackgroundService service = CreateService(checker, messenger, updatesEnabled: false);

        await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, checker.CallCount);
        Assert.False(received);
    }

    [Fact]
    public async Task RunOnce_WhenUpToDate_DoesNotPublish()
    {
        FakeUpdateChecker checker = new(UpdateCheckResult.UpToDate(Current, Current));
        StrongReferenceMessenger messenger = new();
        bool received = false;
        messenger.Register<UpdateAvailableMessage>(this, (_, _) => received = true);
        using UpdateCheckBackgroundService service = CreateService(checker, messenger, updatesEnabled: true);

        await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, checker.CallCount);
        Assert.False(received);
    }

    /// <summary>Gibt die erzeugten ServiceProvider frei.</summary>
    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables)
        {
            disposable.Dispose();
        }
    }

    private UpdateCheckBackgroundService CreateService(
        FakeUpdateChecker checker,
        IMessenger messenger,
        bool updatesEnabled)
    {
        ServiceCollection services = new();
        _ = services.AddScoped<IUpdateChecker>(_ => checker);
        ServiceProvider provider = services.BuildServiceProvider();
        _disposables.Add(provider);
        StubSettingsService settings = new(updatesEnabled);
        return new UpdateCheckBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            settings,
            messenger,
            TimeProvider.System,
            NullLogger<UpdateCheckBackgroundService>.Instance);
    }

    private sealed class FakeUpdateChecker : IUpdateChecker
    {
        private readonly UpdateCheckResult _result;

        public FakeUpdateChecker(UpdateCheckResult result) => _result = result;

        public int CallCount { get; private set; }

        public Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class StubSettingsService : ISettingsService
    {
        public StubSettingsService(bool checkForUpdates) =>
            Current = AppSettings.Default with
            {
                Behavior = BehaviorSettings.Default with { CheckForUpdatesAtStartup = checkForUpdates },
            };

        public AppSettings Current { get; }

        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged
        {
            add { }
            remove { }
        }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
