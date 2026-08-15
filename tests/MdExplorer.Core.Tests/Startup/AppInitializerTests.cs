using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Core.Startup;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.Core.Tests.Startup;

public sealed class AppInitializerTests
{
    /// <summary>
    /// Schrittweite, in der die Testuhr vorgestellt wird, bis der beobachtete Ablauf endet.
    /// </summary>
    private static readonly TimeSpan AdvanceStep = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Echtes Zeitbudget als Notausstieg. Läuft es ab, scheitert der Test mit einer
    /// Zeitüberschreitung — statt den ganzen Lauf anzuhalten.
    /// </summary>
    private static readonly TimeSpan RealTimeBudget = TimeSpan.FromSeconds(10);

    /// <summary>Obergrenze der Vorstell-Schritte, damit die Schleife immer endet.</summary>
    private const int MaxAdvanceAttempts = 40;

    [Fact]
    public async Task InitializeAsync_WhenMigrationIsFastAndMinimumDurationNotReached_WaitsRemainingTime()
    {
        FakeTimeProvider timeProvider = new();
        FakeMigrator migrator = new(migrationDuration: TimeSpan.FromMilliseconds(100), timeProvider);
        ILogger<AppInitializer> logger = NullLogger<AppInitializer>.Instance;
        AppInitializer sut = new(migrator, new FakeSettingsService(), logger, timeProvider);

        Task initialization = sut.InitializeAsync(TimeSpan.FromMilliseconds(1500), CancellationToken.None);

        await AdvanceUntilCompletedAsync(timeProvider, migrator.WaitUntilCompletedAsync()).ConfigureAwait(true);
        await AdvanceUntilCompletedAsync(timeProvider, initialization).ConfigureAwait(true);

        Assert.True(migrator.WasCalled);
    }

    [Fact]
    public async Task InitializeAsync_WhenMigrationExceedsMinimumDuration_DoesNotWaitFurther()
    {
        FakeTimeProvider timeProvider = new();
        FakeMigrator migrator = new(migrationDuration: TimeSpan.FromSeconds(3), timeProvider);
        ILogger<AppInitializer> logger = NullLogger<AppInitializer>.Instance;
        AppInitializer sut = new(migrator, new FakeSettingsService(), logger, timeProvider);

        Task initialization = sut.InitializeAsync(TimeSpan.FromMilliseconds(1500), CancellationToken.None);

        await AdvanceUntilCompletedAsync(timeProvider, migrator.WaitUntilCompletedAsync()).ConfigureAwait(true);

        // Ohne weiteres Vorstellen der Uhr fertig werden — das ist die Aussage dieses Tests:
        // Die Migration hat die Mindestdauer bereits überschritten, es wird nicht mehr gewartet.
        await initialization.WaitAsync(RealTimeBudget).ConfigureAwait(true);

        Assert.True(migrator.WasCalled);
    }

    /// <summary>
    /// Stellt die Testuhr schrittweise vor, bis <paramref name="task"/> abgeschlossen ist.
    /// </summary>
    /// <remarks>
    /// Ein einzelnes Vorstellen an einer geratenen Stelle genügt nicht: Wer die Uhr
    /// vorstellt, bevor der Wartende seinen Timer angemeldet hat, stellt an ihm vorbei —
    /// der Timer beginnt danach von vorn, und die Zeit, auf die er wartet, kommt nie.
    /// Genau daran blieb am 11.08.2026 ein vollständiger Testlauf dreizehn Minuten stehen,
    /// während die Zusammenfassung daneben „bestanden" für die übrigen Tests meldete.
    /// Das <see cref="Task.Yield"/> vor jedem Schritt gibt den Fortsetzungen Gelegenheit,
    /// ihre Timer zu setzen.
    /// </remarks>
    private static async Task AdvanceUntilCompletedAsync(FakeTimeProvider timeProvider, Task task)
    {
        for (int attempt = 0; attempt < MaxAdvanceAttempts && !task.IsCompleted; attempt++)
        {
            await Task.Yield();
            timeProvider.Advance(AdvanceStep);
        }

        await task.WaitAsync(RealTimeBudget).ConfigureAwait(true);
    }

    [Fact]
    public async Task InitializeAsync_OnNegativeMinimumDuration_Throws()
    {
        FakeTimeProvider timeProvider = new();
        FakeMigrator migrator = new(migrationDuration: TimeSpan.Zero, timeProvider);
        AppInitializer sut = new(migrator, new FakeSettingsService(), NullLogger<AppInitializer>.Instance, timeProvider);

        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => sut.InitializeAsync(TimeSpan.FromMilliseconds(-1), CancellationToken.None)).ConfigureAwait(true);
    }

    private sealed class FakeMigrator(TimeSpan migrationDuration, FakeTimeProvider timeProvider) : IDatabaseMigrator
    {
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasCalled { get; private set; }

        public async Task MigrateAsync(CancellationToken cancellationToken)
        {
            WasCalled = true;
            await Task.Delay(migrationDuration, timeProvider, cancellationToken).ConfigureAwait(true);
            _ = _completed.TrySetResult();
        }

        public Task WaitUntilCompletedAsync() => _completed.Task;
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; private set; } = AppSettings.Default;

        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            AppSettings previous = Current;
            Current = settings;
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(previous, settings));
            return Task.CompletedTask;
        }
    }
}
