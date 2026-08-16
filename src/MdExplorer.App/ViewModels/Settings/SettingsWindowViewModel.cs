using System.Globalization;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Core.Settings;
using MdExplorer.Update.Abstractions;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels.Settings;

/// <summary>
/// Orchestriert die drei Tab-ViewModels. Bietet OK/Abbrechen-Commands an und
/// koordiniert Validierung, Persistenz und das Schließen des Fensters.
/// </summary>
internal sealed partial class SettingsWindowViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly SettingsValidator _validator;
    private readonly IDialogService _dialogService;
    private readonly ILogger<SettingsWindowViewModel> _logger;

    /// <summary>
    /// Der Merker, der beim Beenden der Anwendung anschlägt.
    /// </summary>
    /// <remarks>
    /// Er kommt aus dem Wirt (<c>IHostApplicationLifetime.ApplicationStopping</c>). Ohne ihn
    /// stand hier <c>CancellationToken.None</c> — dann wartet das Schließen auf Arbeit, die
    /// niemand mehr braucht. Ohne Wirt, also im Test, bleibt er der leere Merker.
    /// </remarks>
    private readonly CancellationToken _shutdownToken;

    /// <summary>Erzeugt das ViewModel — bezieht den aktuellen Settings-Stand aus dem Service.</summary>
    public SettingsWindowViewModel(
        ISettingsService settingsService,
        SettingsValidator validator,
        IDialogService dialogService,
        IUpdateChecker updateChecker,
        IUpdateInstaller updateInstaller,
        ILogger<SettingsWindowViewModel> logger,
        CancellationToken shutdownToken = default)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(updateChecker);
        ArgumentNullException.ThrowIfNull(updateInstaller);
        ArgumentNullException.ThrowIfNull(logger);

        _settingsService = settingsService;
        _validator = validator;
        _dialogService = dialogService;
        _logger = logger;
        _shutdownToken = shutdownToken;

        AppSettings current = settingsService.Current;
        Indexing = new IndexingTabViewModel(current.Indexing, dialogService);
        Appearance = new AppearanceTabViewModel(current.Appearance);
        Behavior = new BehaviorTabViewModel(current.Behavior, new UpdateSectionViewModel(updateChecker, updateInstaller));

        ApplyAndCloseCommand = new AsyncRelayCommand(ApplyAndCloseAsync);
        CancelCommand = new RelayCommand(RaiseCloseRequested);
    }

    /// <summary>Wird ausgelöst, wenn das Fenster geschlossen werden soll.</summary>
    public event EventHandler<SettingsCloseEventArgs>? CloseRequested;

    /// <summary>Tab „Indexierung".</summary>
    public IndexingTabViewModel Indexing { get; }

    /// <summary>Tab „Darstellung".</summary>
    public AppearanceTabViewModel Appearance { get; }

    /// <summary>Tab „Verhalten".</summary>
    public BehaviorTabViewModel Behavior { get; }

    /// <summary>Speichert die Settings und schließt das Fenster.</summary>
    public AsyncRelayCommand ApplyAndCloseCommand { get; }

    /// <summary>Verwirft die Eingaben und schließt das Fenster.</summary>
    public RelayCommand CancelCommand { get; }

    private static string FormatValidationErrors(SettingsValidationResult validation)
    {
        StringBuilder builder = new();
        _ = builder.AppendLine(CultureInfo.InvariantCulture, $"{validation.Errors.Length} Fehler:");
        foreach (SettingsValidationError error in validation.Errors)
        {
            _ = builder.AppendLine(CultureInfo.InvariantCulture, $"• [{error.Field}] {error.Message}");
        }
        return builder.ToString();
    }

    [LoggerMessage(EventId = 800, Level = LogLevel.Information, Message = "Settings über Dialog persistiert.")]
    private static partial void LogSettingsSaved(ILogger logger);

    [LoggerMessage(EventId = 801, Level = LogLevel.Error, Message = "Settings konnten nicht persistiert werden.")]
    private static partial void LogSettingsSaveFailed(ILogger logger, Exception exception);

    private async Task ApplyAndCloseAsync()
    {
        AppSettings candidate = new(
            AppSettings.CurrentSchemaVersion,
            Indexing.ToSettings(),
            Appearance.ToSettings(),
            Behavior.ToSettings());

        SettingsValidationResult validation = _validator.Validate(candidate);
        if (!validation.IsValid)
        {
            string message = FormatValidationErrors(validation);
            _dialogService.ShowError("Einstellungen ungültig", message);
            return;
        }

        try
        {
            await _settingsService.SaveAsync(candidate, _shutdownToken).ConfigureAwait(true);
            LogSettingsSaved(_logger);
            CloseRequested?.Invoke(this, new SettingsCloseEventArgs(true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSettingsSaveFailed(_logger, ex);
            _dialogService.ShowError("Speichern fehlgeschlagen", ex.Message);
        }
    }

    private void RaiseCloseRequested() => CloseRequested?.Invoke(this, new SettingsCloseEventArgs(false));
}

/// <summary>Eventdaten zum Schließen des Settings-Fensters.</summary>
internal sealed class SettingsCloseEventArgs : EventArgs
{
    /// <summary>Erzeugt das Event.</summary>
    public SettingsCloseEventArgs(bool savedChanges) => SavedChanges = savedChanges;

    /// <summary><see langword="true"/>, wenn die Änderungen persistiert wurden.</summary>
    public bool SavedChanges { get; }
}
