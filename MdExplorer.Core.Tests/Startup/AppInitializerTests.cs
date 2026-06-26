using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Core.Startup;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.Core.Tests.Startup;

public sealed class AppInitializerTests
{
    [Fact]
    public async Task InitializeAsync_WhenMigrationIsFastAndMinimumDurationNotReached_WaitsRemainingTime()
    {
        FakeTimeProvider timeProvider = new();
        FakeMigrator migrator = new(migrationDuration: TimeSpan.FromMilliseconds(100), timeProvider);
        ILogger<AppInitializer> logger = NullLogger<AppInitializer>.Instance;
        AppInitializer sut = new(migrator, new FakeSettingsService(), logger, timeProvider);

        Task initialization = sut.InitializeAsync(TimeSpan.FromMilliseconds(1500), CancellationToken.None);

        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        await migrator.WaitUntilCompletedAsync().ConfigureAwait(true);
        timeProvider.Advance(TimeSpan.FromMilliseconds(1400));
        await initialization.ConfigureAwait(true);

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

        timeProvider.Advance(TimeSpan.FromSeconds(3));
        await migrator.WaitUntilCompletedAsync().ConfigureAwait(true);
        await initialization.ConfigureAwait(true);

        Assert.True(migrator.WasCalled);
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
