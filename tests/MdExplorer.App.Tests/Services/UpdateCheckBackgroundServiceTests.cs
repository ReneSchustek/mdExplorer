using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.Messaging;
using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.Core.Models;
using MdExplorer.Update.Abstractions;
using MdExplorer.Update.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Die Update-Prüfung beim Start läuft nur, wenn sie eingeschaltet ist, meldet sich nur bei
/// einer neueren Fassung und endet ohne Fehler, wenn die Anwendung während des Startversatzes
/// schon wieder schließt oder die Prüfung selbst scheitert.
/// </summary>
/// <remarks>
/// Der Startversatz läuft über eine Testuhr. Sie wird erst vorgestellt, wenn der Dienst auf ihr
/// wartet; dann löst das Vorstellen die Prüfung sicher aus, ohne Schleife und ohne Wanduhr.
/// </remarks>
public sealed class UpdateCheckBackgroundServiceTests : IDisposable
{
    private static readonly SemanticVersion Current = new(0, 9, 0);
    private static readonly SemanticVersion Newer = new(1, 0, 0);
    private static readonly Uri ReleaseUrl = new("https://github.com/ReneSchustek/mdExplorer/releases/latest");
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);

    private readonly List<IDisposable> _disposables = [];

    [Fact]
    public async Task RunOnce_WhenUpdateAvailable_PublishesMessage()
    {
        FakeUpdateChecker checker = new(UpdateCheckResult.Available(Current, Newer, ReleaseUrl));
        StrongReferenceMessenger messenger = new();
        UpdateAvailableMessage? received = null;
        messenger.Register<UpdateAvailableMessage>(this, (_, message) => received = message);
        using UpdateCheckBackgroundService service = CreateService(checker, messenger, updatesEnabled: true);

        await service.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, checker.CallCount);
        Assert.NotNull(received);
        Assert.Equal("1.0.0", received.Version);
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

        await service.RunOnceAsync(TestContext.Current.CancellationToken);

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

        await service.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, checker.CallCount);
        Assert.False(received);
    }

    [Fact]
    public async Task ExecuteAsync_AfterTheStartupDelay_RunsTheCheck()
    {
        // Der Versatz beim Start gibt Hauptfenster und erstem Indexer-Lauf Vorrang. Die
        // Prüfung muss danach aber tatsächlich laufen, sonst fände der Nutzer nie
        // heraus, dass eine neue Fassung bereitsteht.
        FakeUpdateChecker checker = new(UpdateCheckResult.Available(Current, Newer, ReleaseUrl));
        SignalingTimeProvider time = new();
        using UpdateCheckBackgroundService service = CreateService(checker, new StrongReferenceMessenger(), updatesEnabled: true, time);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await time.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        time.Advance(StartupDelay);
        await checker.Called.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, checker.CallCount);
        Assert.NotNull(service.ExecuteTask);
        Assert.False(service.ExecuteTask.IsFaulted);
    }

    [Fact]
    public async Task ExecuteAsync_WhenStoppedDuringTheStartupDelay_EndsWithoutFault()
    {
        // Wer die Anwendung sofort wieder schließt, darf keinen Fehler im Protokoll finden.
        FakeUpdateChecker checker = new(UpdateCheckResult.Available(Current, Newer, ReleaseUrl));
        SignalingTimeProvider time = new();
        using UpdateCheckBackgroundService service = CreateService(checker, new StrongReferenceMessenger(), updatesEnabled: true, time);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, checker.CallCount);
        Assert.NotNull(service.ExecuteTask);
        Assert.True(service.ExecuteTask.IsCompleted);
        Assert.False(service.ExecuteTask.IsFaulted);
    }

    /// <remarks>
    /// Die Zusage „nicht-fatal“ darf nicht an der Fangliste des Prüfers hängen. Endet
    /// <c>ExecuteAsync</c> mit einer Ausnahme, fährt der Host mit seiner Voreinstellung die ganze
    /// Anwendung herunter, und eine gescheiterte Versionsabfrage schlösse das Programm.
    /// </remarks>
    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(System.Net.Http.HttpRequestException))]
    [InlineData(typeof(TaskCanceledException))]
    public async Task ExecuteAsync_WhenTheCheckFails_EndsWithoutFault(Type failure)
    {
        FakeUpdateChecker checker = new(UpdateCheckResult.UpToDate(Current, Current))
        {
            Failure = Assert.IsAssignableFrom<Exception>(Activator.CreateInstance(failure, "simuliert")),
        };
        SignalingTimeProvider time = new();
        using UpdateCheckBackgroundService service = CreateService(checker, new StrongReferenceMessenger(), updatesEnabled: true, time);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await time.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        time.Advance(StartupDelay);
        Assert.NotNull(service.ExecuteTask);
        Exception? ended = await Record.ExceptionAsync(
            () => service.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Null(ended);
        Assert.Equal(1, checker.CallCount);
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
        FakeSettingsService settings = new(AppSettings.Default with
        {
            Behavior = BehaviorSettings.Default with { CheckForUpdatesAtStartup = updatesEnabled },
        });
        return new UpdateCheckBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            settings,
            messenger,
            timeProvider ?? TimeProvider.System,
            NullLogger<UpdateCheckBackgroundService>.Instance);
    }
}
