using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Core.Settings;
using MdExplorer.Core.Tests.Fakes;
using static MdExplorer.Core.Tests.Settings.SettingsServiceFactory;

namespace MdExplorer.Core.Tests.Settings;

/// <summary>
/// Nur eine echte Änderung meldet sich, und zwar über den Kontext des Aufrufers. Der gemeldete
/// Vorher-Stand stimmt auch dann, wenn zwei Speichervorgänge sich überlappen, und ein
/// fehlerhafter Empfänger beendet nicht den Prozess.
/// </summary>
public sealed class JsonSettingsServiceEventTests
{
    private static readonly AppSettings Debounce1000 =
        AppSettings.Default with { Behavior = AppSettings.Default.Behavior with { SearchDebounceMs = 1000 } };

    [Fact]
    public async Task SaveAsync_OnDifferingPayload_PostsThroughTheCallersContext()
    {
        using JsonSettingsService sut = Create(new InMemoryFileSystem());
        QueueingSynchronizationContext context = new();
        List<SettingsChangedEventArgs> received = [];
        sut.SettingsChanged += (_, args) => received.Add(args);

        await context.RunWithinAsync(() => sut.SaveAsync(Debounce1000, TestContext.Current.CancellationToken)).ConfigureAwait(true);

        // Noch nicht zugestellt: Der Empfänger läuft erst auf dem Kontext, nicht auf dem Worker.
        Assert.Empty(received);
        context.DrainPending();
        SettingsChangedEventArgs delivered = Assert.Single(received);
        Assert.Equal(300, delivered.Previous.Behavior.SearchDebounceMs);
        Assert.Equal(1000, delivered.Current.Behavior.SearchDebounceMs);
    }

    [Fact]
    public async Task SaveAsync_OnIdenticalPayload_PostsNothing()
    {
        using JsonSettingsService sut = Create(new InMemoryFileSystem());
        await sut.SaveAsync(AppSettings.Default, TestContext.Current.CancellationToken).ConfigureAwait(true);
        sut.SettingsChanged += (_, _) => { };
        QueueingSynchronizationContext context = new();

        await context.RunWithinAsync(() => sut.SaveAsync(AppSettings.Default, TestContext.Current.CancellationToken)).ConfigureAwait(true);

        // Ohne Wanduhr: Jede Zustellung liefe über diesen Kontext; es gibt keine.
        Assert.Equal(0, context.PostCount);
    }

    [Fact]
    public async Task SaveAsync_WithoutSyncContext_DeliversOnTheThreadPool()
    {
        using JsonSettingsService sut = Create(new InMemoryFileSystem());
        TaskCompletionSource<int> handlerCalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.SettingsChanged += (_, _) => handlerCalled.TrySetResult(Environment.CurrentManagedThreadId);

        await Task.Run(() => sut.SaveAsync(Debounce1000, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken).ConfigureAwait(true);
        int handlerThreadId = await handlerCalled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(handlerThreadId > 0);
    }

    [Fact]
    public async Task SaveAsync_WhenTheSubscriberThrowsOnTheThreadPool_DoesNotTakeTheProcessDown()
    {
        // Eine Ausnahme auf einem ThreadPool-Thread beendet den Prozess. Ein fehlerhafter
        // Empfänger soll ins Protokoll laufen, nicht die Anwendung beenden.
        RecordingLogger<JsonSettingsService> logger = new();
        using JsonSettingsService sut = Create(new InMemoryFileSystem(), logger: logger);
        sut.SettingsChanged += (_, _) => throw new InvalidOperationException("Empfänger kaputt");

        await Task.Run(() => sut.SaveAsync(Debounce1000, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken).ConfigureAwait(true);
        Exception? logged = await logger.FirstError.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = Assert.IsType<InvalidOperationException>(logged);
    }

    [Fact]
    public async Task SaveAsync_WhenTwoSavesOverlap_ReportsTheRealPreviousState()
    {
        // Beide Vorgänge lasen früher den alten Stand vor der Sperre. Der zweite meldete dann
        // denselben Vorher-Wert wie der erste, und der Verlauf zeigte einen Sprung, den es nie gab.
        InMemoryFileSystem fileSystem = new();
        using JsonSettingsService sut = Create(fileSystem);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource firstWriting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fileSystem.HoldNextWrite(release.Task, firstWriting);
        List<SettingsChangedEventArgs> received = [];
        Lock gate = new();
        TaskCompletionSource bothDelivered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.SettingsChanged += (_, args) =>
        {
            lock (gate)
            {
                received.Add(args);
                if (received.Count == 2)
                {
                    _ = bothDelivered.TrySetResult();
                }
            }
        };
        AppSettings dark = AppSettings.Default with { Appearance = AppearanceSettings.Default with { Theme = AppTheme.Dark } };

        Task first = Task.Run(() => sut.SaveAsync(Debounce1000, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        await firstWriting.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(true);
        Task second = Task.Run(() => sut.SaveAsync(Debounce1000 with { Appearance = dark.Appearance }, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(true);
        await bothDelivered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(true);

        SettingsChangedEventArgs themeChange = Assert.Single(received, args => args.Current.Appearance.Theme == AppTheme.Dark);
        Assert.Equal(1000, themeChange.Previous.Behavior.SearchDebounceMs);
    }
}
