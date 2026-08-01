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

    [Fact]
    public async Task ExecuteAsync_AfterTheStartupDelay_RunsTheCheck()
    {
        // Der Versatz beim Start gibt Hauptfenster und erstem Indexer-Lauf Vorrang. Die
        // Prüfung muss danach aber tatsächlich laufen — sonst fände der Nutzer nie
        // heraus, dass eine neue Fassung bereitsteht.
        FakeUpdateChecker checker = new(UpdateCheckResult.Available(Current, Newer, ReleaseUrl));
        StrongReferenceMessenger messenger = new();
        Microsoft.Extensions.Time.Testing.FakeTimeProvider zeit = new();
        using UpdateCheckBackgroundService service = CreateService(checker, messenger, updatesEnabled: true, zeit);

        await service.StartAsync(CancellationToken.None);
        bool gelaufen = await WarteAufAsync(zeit, () => checker.CallCount > 0);
        await service.StopAsync(CancellationToken.None);

        Assert.True(gelaufen, "Die Update-Prüfung ist nach dem Startversatz nicht gelaufen.");
        Assert.False(service.ExecuteTask!.IsFaulted);
    }

    [Fact]
    public async Task ExecuteAsync_WhenStoppedDuringTheStartupDelay_EndsWithoutFault()
    {
        // Wer die Anwendung sofort wieder schließt, darf keinen Fehler im Protokoll finden.
        FakeUpdateChecker checker = new(UpdateCheckResult.Available(Current, Newer, ReleaseUrl));
        StrongReferenceMessenger messenger = new();
        Microsoft.Extensions.Time.Testing.FakeTimeProvider zeit = new();
        using UpdateCheckBackgroundService service = CreateService(checker, messenger, updatesEnabled: true, zeit);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, checker.CallCount);
        Assert.True(service.ExecuteTask!.IsCompleted);
        Assert.False(service.ExecuteTask.IsFaulted);
    }

    /// <summary>Stellt die Uhr schrittweise vor, bis die Bedingung erfüllt ist.</summary>
    private static async Task<bool> WarteAufAsync(
        Microsoft.Extensions.Time.Testing.FakeTimeProvider zeit,
        Func<bool> bedingung)
    {
        DateTimeOffset ende = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < ende)
        {
            if (bedingung())
            {
                return true;
            }
            zeit.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(5).ConfigureAwait(false);
        }
        return bedingung();
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
        bool updatesEnabled,
        TimeProvider? timeProvider = null)
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
            timeProvider ?? TimeProvider.System,
            NullLogger<UpdateCheckBackgroundService>.Instance);
    }

    private sealed class FakeUpdateChecker : IUpdateChecker
    {
        private readonly UpdateCheckResult _result;

        public FakeUpdateChecker(UpdateCheckResult result) => _result = result;

        public int CallCount { get; private set; }

        public bool LastForce { get; private set; }

        public Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken) =>
            CheckForUpdateAsync(force: false, cancellationToken);

        public Task<UpdateCheckResult> CheckForUpdateAsync(bool force, CancellationToken cancellationToken)
        {
            CallCount++;
            LastForce = force;
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
