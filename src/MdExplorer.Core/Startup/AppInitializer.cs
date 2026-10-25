using MdExplorer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Core.Startup;

/// <summary>
/// Orchestriert die Initialisierungsphase der Anwendung: legt die Anwendungsverzeichnisse an,
/// lädt die Settings, wendet ausstehende Datenbank-Migrationen an und wartet danach, bis die
/// Mindest-Anzeigedauer des SplashScreens erreicht ist.
/// </summary>
/// <remarks>
/// Die Verzeichnisse entstehen hier und nicht nebenbei in den Pfad-Gettern: SQLite legt die
/// Datenbankdatei an, aber nicht ihren Ordner, und die Migration ist der erste Schreibzugriff.
/// </remarks>
public sealed partial class AppInitializer(
    IDatabaseMigrator migrator,
    ISettingsService settingsService,
    IFileSystem fileSystem,
    AppPathLayout paths,
    ILogger<AppInitializer> logger,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Standard-Mindestdauer für die SplashScreen-Anzeige. Bei einer kurzen Migration flackerte
    /// der SplashScreen sonst nur auf; anderthalb Sekunden reichen, um ihn wahrzunehmen.
    /// </summary>
    public static readonly TimeSpan DefaultMinimumDisplayDuration = TimeSpan.FromMilliseconds(1500);

    private readonly IDatabaseMigrator _migrator = migrator ?? throw new ArgumentNullException(nameof(migrator));
    private readonly ISettingsService _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly AppPathLayout _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ILogger<AppInitializer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>
    /// Führt die Initialisierung aus und gibt erst zurück, wenn
    /// (a) die Settings geladen sind, (b) die Migration durch ist und (c) die Mindestdauer abgelaufen ist.
    /// </summary>
    public async Task InitializeAsync(TimeSpan minimumDisplayDuration, CancellationToken cancellationToken)
    {
        if (minimumDisplayDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumDisplayDuration), "Darf nicht negativ sein.");
        }

        long startedAtTicks = _timeProvider.GetTimestamp();
        LogInitializationStarted(_logger, minimumDisplayDuration.TotalMilliseconds);

        _paths.EnsureDirectories(_fileSystem);
        _ = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        await _migrator.MigrateAsync(cancellationToken).ConfigureAwait(false);

        TimeSpan elapsed = _timeProvider.GetElapsedTime(startedAtTicks);
        TimeSpan remaining = minimumDisplayDuration - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            LogMigrationCompletedWaiting(_logger, elapsed.TotalMilliseconds, remaining.TotalMilliseconds);
            await Task.Delay(remaining, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            LogMigrationCompletedNoWait(_logger, elapsed.TotalMilliseconds);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Initialisierung gestartet, Mindestdauer {MinimumMs} ms.")]
    private static partial void LogInitializationStarted(ILogger logger, double minimumMs);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Migration in {ElapsedMs} ms abgeschlossen, warte noch {RemainingMs} ms.")]
    private static partial void LogMigrationCompletedWaiting(ILogger logger, double elapsedMs, double remainingMs);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Migration in {ElapsedMs} ms abgeschlossen — Mindestdauer bereits erreicht.")]
    private static partial void LogMigrationCompletedNoWait(ILogger logger, double elapsedMs);
}
