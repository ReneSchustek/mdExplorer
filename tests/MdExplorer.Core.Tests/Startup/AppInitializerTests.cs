using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Core.Startup;
using MdExplorer.Core.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.Core.Tests.Startup;

/// <summary>
/// Der Start dauert mindestens so lange wie verlangt, damit das Startbild nicht nur aufblitzt,
/// aber keinen Augenblick länger, wenn die Migration selbst schon länger gebraucht hat.
/// </summary>
public sealed class AppInitializerTests
{
    private static readonly AppPathLayout Paths = new(@"C:\Benutzer\Test\AppData\Local\MdExplorer");

    /// <summary>
    /// Schrittweite, in der die Testuhr vorgestellt wird, bis der beobachtete Ablauf endet.
    /// Ein Schritt darf über das Ziel hinausgehen, weil die Schleife erst endet, wenn der Ablauf
    /// fertig ist; die Größe bestimmt nur, wie viele Durchläufe das braucht.
    /// </summary>
    private static readonly TimeSpan AdvanceStep = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Echtes Zeitbudget als Notausstieg. Läuft es ab, scheitert der Test mit einer
    /// Zeitüberschreitung, statt den ganzen Lauf anzuhalten.
    /// </summary>
    private static readonly TimeSpan RealTimeBudget = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Obergrenze der Vorstell-Schritte, damit die Schleife immer endet. Vierzig Schritte sind
    /// zwanzig Sekunden Testzeit, ein Vielfaches der längsten simulierten Migration von drei.
    /// </summary>
    private const int MaxAdvanceAttempts = 40;

    [Fact]
    public async Task InitializeAsync_WhenMigrationIsFastAndMinimumDurationNotReached_WaitsRemainingTime()
    {
        // Die Migration ist sofort fertig; das Warten auf die Mindestdauer ist dann das Einzige,
        // was den Start aufhält. Fiele es weg, wäre die Initialisierung schon nach 1.400 ms fertig.
        FakeTimeProvider timeProvider = new();
        FakeMigrator migrator = new(migrationDuration: TimeSpan.Zero, timeProvider);
        ILogger<AppInitializer> logger = NullLogger<AppInitializer>.Instance;
        AppInitializer sut = new(migrator, new FakeSettingsService(), new InMemoryFileSystem(), Paths, logger, timeProvider);

        Task initialization = sut.InitializeAsync(TimeSpan.FromMilliseconds(1500), TestContext.Current.CancellationToken);
        timeProvider.Advance(TimeSpan.FromMilliseconds(1400));
        bool completedEarly = initialization.IsCompleted;
        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        await initialization.WaitAsync(RealTimeBudget, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(completedEarly);
        Assert.True(migrator.WasCalled);
    }

    [Fact]
    public async Task InitializeAsync_CreatesTheApplicationFoldersBeforeTheDatabaseIsTouched()
    {
        // SQLite legt die Datei an, nicht ihren Ordner. Auf einem frischen Rechner scheiterte die
        // erste Migration, wenn niemand den Ordner vorher anlegt.
        FakeTimeProvider timeProvider = new();
        InMemoryFileSystem fileSystem = new();
        bool folderExistedAtMigration = false;
        FakeMigrator migrator = new(TimeSpan.Zero, timeProvider, () => folderExistedAtMigration = fileSystem.DirectoryExists(Paths.ApplicationDataDirectory));
        AppInitializer sut = new(migrator, new FakeSettingsService(), fileSystem, Paths, NullLogger<AppInitializer>.Instance, timeProvider);

        await sut.InitializeAsync(TimeSpan.Zero, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(folderExistedAtMigration);
        Assert.True(fileSystem.DirectoryExists(Paths.WebView2DataDirectory));
    }

    [Fact]
    public async Task InitializeAsync_WhenMigrationExceedsMinimumDuration_DoesNotWaitFurther()
    {
        FakeTimeProvider timeProvider = new();
        FakeMigrator migrator = new(migrationDuration: TimeSpan.FromSeconds(3), timeProvider);
        ILogger<AppInitializer> logger = NullLogger<AppInitializer>.Instance;
        AppInitializer sut = new(migrator, new FakeSettingsService(), new InMemoryFileSystem(), Paths, logger, timeProvider);

        Task initialization = sut.InitializeAsync(TimeSpan.FromMilliseconds(1500), TestContext.Current.CancellationToken);

        await AdvanceUntilCompletedAsync(timeProvider, migrator.WaitUntilCompletedAsync()).ConfigureAwait(true);

        // Ohne weiteres Vorstellen der Uhr fertig werden. Das ist die Aussage dieses Tests:
        // Die Migration hat die Mindestdauer bereits überschritten, es wird nicht mehr gewartet.
        await initialization.WaitAsync(RealTimeBudget, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(migrator.WasCalled);
    }

    /// <summary>
    /// Stellt die Testuhr schrittweise vor, bis <paramref name="task"/> abgeschlossen ist.
    /// </summary>
    /// <remarks>
    /// Ein einzelnes Vorstellen an einer geratenen Stelle genügt nicht: Wer die Uhr
    /// vorstellt, bevor der Wartende seinen Timer angemeldet hat, stellt an ihm vorbei;
    /// der Timer beginnt danach von vorn, und die Zeit, auf die er wartet, kommt nie.
    /// Ein vollständiger Testlauf bliebe daran minutenlang stehen, während die Zusammenfassung
    /// daneben „bestanden" für die übrigen Tests meldet.
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
        AppInitializer sut = new(migrator, new FakeSettingsService(), new InMemoryFileSystem(), Paths, NullLogger<AppInitializer>.Instance, timeProvider);

        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => sut.InitializeAsync(TimeSpan.FromMilliseconds(-1), TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    /// <summary>Migration, die auf der Testuhr eine vorgegebene Zeit braucht.</summary>
    private sealed class FakeMigrator(TimeSpan migrationDuration, FakeTimeProvider timeProvider, Action? onMigrate = null) : IDatabaseMigrator
    {
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasCalled { get; private set; }

        public async Task MigrateAsync(CancellationToken cancellationToken)
        {
            WasCalled = true;
            onMigrate?.Invoke();
            await Task.Delay(migrationDuration, timeProvider, cancellationToken).ConfigureAwait(true);
            _ = _completed.TrySetResult();
        }

        public Task WaitUntilCompletedAsync() => _completed.Task;
    }

    /// <summary>Einstellungen im Speicher, ab Werk mit <see cref="AppSettings.Default"/>.</summary>
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
