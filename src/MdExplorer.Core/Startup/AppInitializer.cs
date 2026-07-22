using MdExplorer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace MdExplorer.Core.Startup;

/// <summary>
/// Orchestriert die Initialisierungsphase der Anwendung:
/// lädt die Settings, startet die Datenbank-Migration und stellt sicher, dass die
/// Mindest-Anzeigedauer des SplashScreens nicht unterschritten wird.
/// </summary>
public sealed partial class AppInitializer(
    IDatabaseMigrator migrator,
    ISettingsService settingsService,
    ILogger<AppInitializer> logger,
    TimeProvider timeProvider)
{
    /// <summary>Standard-Mindestdauer für die SplashScreen-Anzeige.</summary>
    public static readonly TimeSpan DefaultMinimumDisplayDuration = TimeSpan.FromMilliseconds(1500);

    private readonly IDatabaseMigrator _migrator = migrator ?? throw new ArgumentNullException(nameof(migrator));
    private readonly ISettingsService _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
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
