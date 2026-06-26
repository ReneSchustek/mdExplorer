using System.Data.Common;
using System.IO;
using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Messaging;
using MdExplorer.Indexer.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Services;

/// <summary>
/// Bindet das C#-<see cref="ISettingsService.SettingsChanged"/>-Event an den
/// <see cref="IMessenger"/>-basierten <see cref="SettingsChangedMessage"/>-Kanal und
/// triggert nach jeder Settings-Änderung einen Full-Rescan über <see cref="IIndexer.RunInitialScanAsync"/>.
/// Wird als <see cref="IHostedService"/> registriert, damit das Abonnement an den Anwendungs-Lebenszyklus geknüpft ist.
/// </summary>
internal sealed partial class SettingsChangeBridge : IHostedService
{
    private readonly ISettingsService _settingsService;
    private readonly IMessenger _messenger;
    private readonly IIndexer _indexer;
    private readonly ILogger<SettingsChangeBridge> _logger;
    private CancellationTokenSource? _rescanCts;

    /// <summary>Erzeugt die Bridge und löst Pflichtabhängigkeiten auf.</summary>
    public SettingsChangeBridge(
        ISettingsService settingsService,
        IMessenger messenger,
        IIndexer indexer,
        ILogger<SettingsChangeBridge> logger)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(indexer);
        ArgumentNullException.ThrowIfNull(logger);

        _settingsService = settingsService;
        _messenger = messenger;
        _indexer = indexer;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _rescanCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _settingsService.SettingsChanged += OnSettingsChanged;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _settingsService.SettingsChanged -= OnSettingsChanged;
        CancellationTokenSource? cts = _rescanCts;
        _rescanCts = null;
        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
            cts.Dispose();
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _ = _messenger.Send(new SettingsChangedMessage(args.Previous, args.Current));
        LogSettingsChanged(_logger);
        _ = TriggerRescanAsync();
    }

    private async Task TriggerRescanAsync()
    {
        CancellationToken token = _rescanCts?.Token ?? CancellationToken.None;
        try
        {
            await _indexer.RunInitialScanAsync(token).ConfigureAwait(false);
            LogRescanCompleted(_logger);
        }
        catch (OperationCanceledException)
        {
            // Anwendung wird heruntergefahren — kein Fehler.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbException or ArgumentException or InvalidOperationException)
        {
            // DbException ergaenzt — Settings-Rescan ueberlebt SQLite-Spitzen.
            // Argument-/InvalidOperationException ergaenzt — falls der Indexer
            // (jetzt mit Watchdog) doch noch eine Library-Exception durchreicht, bleibt
            // der naechste Settings-Save / Rescan funktional.
            LogRescanFailed(_logger, ex);
        }
    }

    [LoggerMessage(EventId = 900, Level = LogLevel.Information, Message = "Settings geändert — Full-Rescan wird ausgelöst.")]
    private static partial void LogSettingsChanged(ILogger logger);

    [LoggerMessage(EventId = 901, Level = LogLevel.Information, Message = "Full-Rescan nach Settings-Änderung abgeschlossen.")]
    private static partial void LogRescanCompleted(ILogger logger);

    [LoggerMessage(EventId = 902, Level = LogLevel.Warning, Message = "Full-Rescan nach Settings-Änderung fehlgeschlagen.")]
    private static partial void LogRescanFailed(ILogger logger, Exception exception);
}
