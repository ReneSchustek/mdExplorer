using System.Data.Common;
using System.IO;
using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Messaging;
using MdExplorer.Core.Models;
using MdExplorer.Indexer.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Services;

/// <summary>
/// Bindet das C#-<see cref="ISettingsService.SettingsChanged"/>-Event an den
/// <see cref="IMessenger"/>-basierten <see cref="SettingsChangedMessage"/>-Kanal und
/// triggert nach einer Änderung der Index-Einstellungen einen Full-Rescan über
/// <see cref="IIndexer.RunInitialScanAsync"/>.
/// Wird als <see cref="IHostedService"/> registriert, damit das Abonnement an den Anwendungs-Lebenszyklus geknüpft ist.
/// </summary>
/// <remarks>
/// Nur <see cref="AppSettings.Indexing"/> bestimmt, was im Index steht. Ein Wechsel von
/// Erscheinungsbild oder Wartezeiten löst deshalb keinen Durchlauf aus. Kommt eine weitere
/// Änderung, bevor der Durchlauf fertig ist, wird er abgebrochen: Er arbeitet mit einem Stand,
/// der nicht mehr gilt, und der neue liest den Bestand ohnehin ganz.
/// </remarks>
internal sealed partial class SettingsChangeBridge : IHostedService
{
    private readonly ISettingsService _settingsService;
    private readonly IMessenger _messenger;
    private readonly IIndexer _indexer;
    private readonly ILogger<SettingsChangeBridge> _logger;
    private readonly Lock _rescanLock = new();
    private CancellationTokenSource? _lifetime;
    private CancellationTokenSource? _currentRescan;

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
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _settingsService.SettingsChanged += OnSettingsChanged;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _settingsService.SettingsChanged -= OnSettingsChanged;
        CancellationTokenSource? lifetime = _lifetime;
        _lifetime = null;
        if (lifetime is not null)
        {
            // Der Abbruch der Lebensdauer erreicht über die Verknüpfung auch den laufenden Durchlauf.
            await lifetime.CancelAsync().ConfigureAwait(false);
            lifetime.Dispose();
        }
    }

    /// <summary>Ob sich zwischen zwei Ständen etwas geändert hat, das den Inhalt des Index bestimmt.</summary>
    /// <remarks>
    /// Die Listen werden nach Inhalt verglichen: Nach dem Laden aus der Datei sind es immer neue
    /// Objekte, auch wenn nichts anders ist.
    /// </remarks>
    private static bool AffectsIndex(IndexingSettings previous, IndexingSettings current) =>
        previous.AutoExtractHashtags != current.AutoExtractHashtags
        || !previous.Roots.SequenceEqual(current.Roots, StringComparer.Ordinal)
        || !previous.ExclusionPatterns.SequenceEqual(current.ExclusionPatterns, StringComparer.Ordinal)
        || !previous.UiExcludedFolders.SequenceEqual(current.UiExcludedFolders, StringComparer.Ordinal);

    [LoggerMessage(EventId = 900, Level = LogLevel.Information, Message = "Index-Einstellungen geändert — Full-Rescan wird ausgelöst.")]
    private static partial void LogSettingsChanged(ILogger logger);

    [LoggerMessage(EventId = 901, Level = LogLevel.Information, Message = "Full-Rescan nach Settings-Änderung abgeschlossen.")]
    private static partial void LogRescanCompleted(ILogger logger);

    [LoggerMessage(EventId = 902, Level = LogLevel.Warning, Message = "Full-Rescan nach Settings-Änderung fehlgeschlagen.")]
    private static partial void LogRescanFailed(ILogger logger, Exception exception);

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _ = _messenger.Send(new SettingsChangedMessage(args.Previous, args.Current));
        if (!AffectsIndex(args.Previous.Indexing, args.Current.Indexing))
        {
            return;
        }

        LogSettingsChanged(_logger);
        _ = RescanAsync();
    }

    private async Task RescanAsync()
    {
        CancellationTokenSource? rescan = ReplaceCurrentRescan();
        if (rescan is null)
        {
            // Nicht gestartet oder schon gestoppt: Es gibt nichts mehr, wofür sich ein Durchlauf lohnte.
            return;
        }

        try
        {
            await _indexer.RunInitialScanAsync(rescan.Token).ConfigureAwait(false);
            LogRescanCompleted(_logger);
        }
        catch (OperationCanceledException)
        {
            // Ein neuerer Durchlauf hat übernommen, oder die Anwendung wird beendet.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbException or ArgumentException or InvalidOperationException)
        {
            // DbException: Der Settings-Rescan überlebt SQLite-Spitzen.
            // Argument-/InvalidOperationException: Reicht der Indexer trotz Watchdog doch
            // eine Library-Exception durch, bleibt der nächste Settings-Save / Rescan funktional.
            LogRescanFailed(_logger, ex);
        }
        finally
        {
            ReleaseRescan(rescan);
        }
    }

    /// <summary>Legt den Abbruchmerker für den neuen Durchlauf an und bricht den laufenden ab.</summary>
    /// <remarks>
    /// Wer einen Merker ersetzt, gibt ihn auch frei, und zwar erst nach dem Abbruch. Der
    /// abgebrochene Durchlauf kann auf demselben Thread bis in sein <c>finally</c> weiterlaufen;
    /// gäbe er dort seinen Merker frei, geschähe das mitten in dessen <c>Cancel</c>. Die Sperre ist
    /// wiedereintrittsfähig, dieser Weg verklemmt also nicht.
    /// </remarks>
    private CancellationTokenSource? ReplaceCurrentRescan()
    {
        lock (_rescanLock)
        {
            if (_lifetime is not { IsCancellationRequested: false } lifetime)
            {
                return null;
            }

            CancellationTokenSource? replaced = _currentRescan;
            _currentRescan = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            replaced?.Cancel();
            replaced?.Dispose();
            return _currentRescan;
        }
    }

    /// <summary>Gibt den Merker eines beendeten Durchlaufs frei, sofern ihn kein neuerer ersetzt hat.</summary>
    private void ReleaseRescan(CancellationTokenSource rescan)
    {
        lock (_rescanLock)
        {
            if (ReferenceEquals(_currentRescan, rescan))
            {
                _currentRescan = null;
                rescan.Dispose();
            }
        }
    }
}
