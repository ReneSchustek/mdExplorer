using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.Messaging;
using MdExplorer.Core.Abstractions;
using MdExplorer.Update.Abstractions;
using MdExplorer.Update.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Services;

/// <summary>
/// Hintergrunddienst, der kurz nach dem Start einmalig auf neue Versionen prüft — sofern der
/// Nutzer es nicht in den Einstellungen abgewählt hat. Das eigentliche Throttling übernimmt der
/// <see cref="IUpdateChecker"/>. Ein verfügbares Update wird per <see cref="IMessenger"/> als
/// <see cref="UpdateAvailableMessage"/> an das UI gemeldet. Der Dienst ist bewusst nicht-fatal:
/// schlägt die Prüfung fehl, bleibt die Anwendung unbeeinträchtigt.
/// </summary>
internal sealed partial class UpdateCheckBackgroundService : BackgroundService
{
    // Kurzer Versatz, damit das Hauptfenster und der erste Indexer-Tick Vorrang haben.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISettingsService _settingsService;
    private readonly IMessenger _messenger;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpdateCheckBackgroundService> _logger;

    /// <summary>Erzeugt den Dienst und löst seine Abhängigkeiten auf.</summary>
    public UpdateCheckBackgroundService(
        IServiceScopeFactory scopeFactory,
        ISettingsService settingsService,
        IMessenger messenger,
        TimeProvider timeProvider,
        ILogger<UpdateCheckBackgroundService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _settingsService = settingsService;
        _messenger = messenger;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, _timeProvider, stoppingToken).ConfigureAwait(false);
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // erwartete Abbruchsemantik beim Herunterfahren
        }
    }

    /// <summary>
    /// Führt genau einen Prüflauf aus: respektiert den Opt-out, holt den <see cref="IUpdateChecker"/>
    /// aus einem eigenen Scope und meldet ein verfügbares Update an das UI. Sichtbar für Tests.
    /// </summary>
    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!_settingsService.Current.Behavior.CheckForUpdatesAtStartup)
        {
            LogDisabled(_logger);
            return;
        }

        UpdateCheckResult result;
        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            IUpdateChecker checker = scope.ServiceProvider.GetRequiredService<IUpdateChecker>();
            result = await checker.CheckForUpdateAsync(cancellationToken).ConfigureAwait(false);
        }

        if (result is { IsUpdateAvailable: true, LatestVersion: { } latest, ReleaseUrl: { } releaseUrl })
        {
            _ = _messenger.Send(new UpdateAvailableMessage(latest.ToString(), releaseUrl));
        }
    }

    [LoggerMessage(EventId = 720, Level = LogLevel.Debug, Message = "Update-Prüfung beim Start ist in den Einstellungen deaktiviert.")]
    private static partial void LogDisabled(ILogger logger);
}
