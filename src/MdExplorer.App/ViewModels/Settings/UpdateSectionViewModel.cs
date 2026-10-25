using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Localization;
using MdExplorer.Update.Abstractions;
using MdExplorer.Update.Models;

namespace MdExplorer.App.ViewModels.Settings;

/// <summary>
/// Steuert den Update-Abschnitt in den Einstellungen: Prüfen, Herunterladen samt
/// Prüfsummen-Kontrolle und Starten des Installationsprogramms.
/// <para>
/// Der Ablauf ist bewusst zweistufig. „Nach Updates suchen" fragt nur ab; installiert wird
/// erst auf ausdrücklichen zweiten Klick. Ein Programm, das sich ungefragt ersetzt, während
/// jemand damit arbeitet, wäre die schlechtere Antwort.
/// </para>
/// </summary>
internal sealed partial class UpdateSectionViewModel : ObservableObject, IDisposable
{
    private readonly IUpdateChecker _checker;
    private readonly IUpdateInstaller _installer;

    /// <summary>
    /// Bricht laufende Vorgänge ab, wenn der Dialog geschlossen wird. Ohne das liefe ein
    /// begonnener Download weiter und meldete am Ende an ein Fenster, das es nicht mehr
    /// gibt; das Installationsprogramm startete dann, ohne dass die Anwendung endet.
    /// </summary>
    private readonly CancellationTokenSource _cancellation = new();

    [ObservableProperty]
    private string _statusText = UiText.Get("Update_Status_NotChecked");

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private bool _isProgressVisible;

    [ObservableProperty]
    private bool _isInstallAvailable;

    private UpdateAsset? _asset;
    private bool _disposed;

    /// <summary>Erzeugt den Abschnitt.</summary>
    public UpdateSectionViewModel(IUpdateChecker checker, IUpdateInstaller installer)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(installer);

        _checker = checker;
        _installer = installer;

        CheckCommand = new AsyncRelayCommand(CheckAsync, () => !IsBusy);
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => !IsBusy && IsInstallAvailable);
    }

    /// <summary>Wird ausgelöst, wenn das Installationsprogramm läuft und die Anwendung enden soll.</summary>
    public event EventHandler? InstallerStarted;

    /// <summary>Löst eine vom Nutzer angeforderte Prüfung aus.</summary>
    public AsyncRelayCommand CheckCommand { get; }

    /// <summary>Lädt das geprüfte Paket und startet es.</summary>
    public AsyncRelayCommand InstallCommand { get; }

    /// <summary>Bricht laufende Vorgänge ab. Wird vom Dialog beim Schließen aufgerufen.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_cancellation.IsCancellationRequested)
        {
            _cancellation.Cancel();
        }

        _cancellation.Dispose();
    }

    /// <summary>Formuliert das Prüfergebnis als Satz für die Oberfläche.</summary>
    private static string DescribeResult(UpdateCheckResult result) => result.Status switch
    {
        UpdateCheckStatus.UpdateAvailable when result.IsInstallable =>
            UiText.Format("Update_Status_Available", result.LatestVersion, result.CurrentVersion),
        UpdateCheckStatus.UpdateAvailable =>
            UiText.Format("Update_Status_AvailableNotInstallable", result.LatestVersion),
        UpdateCheckStatus.UpToDate => UiText.Format("Update_Status_UpToDate", result.CurrentVersion),
        UpdateCheckStatus.Failed => UiText.Get("Update_Status_CheckFailed"),
        _ => UiText.Get("Update_Status_NotChecked"),
    };

    private static string DescribeDownloadFailure(UpdateDownloadStatus status) => status switch
    {
        UpdateDownloadStatus.DownloadFailed => UiText.Get("Update_Download_Failed"),
        UpdateDownloadStatus.ChecksumMismatch => UiText.Get("Update_Download_ChecksumMismatch"),
        UpdateDownloadStatus.NoChecksumPublished => UiText.Get("Update_Download_NoChecksum"),
        UpdateDownloadStatus.StorageFailed => UiText.Get("Update_Download_StorageFailed"),
        UpdateDownloadStatus.NoSignaturePublished => UiText.Get("Update_Download_NoSignature"),
        UpdateDownloadStatus.SignatureInvalid => UiText.Get("Update_Download_SignatureInvalid"),
        _ => UiText.Get("Update_Download_InstallFailed"),
    };

    partial void OnIsBusyChanged(bool value)
    {
        CheckCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsInstallAvailableChanged(bool value) => InstallCommand.NotifyCanExecuteChanged();

    private async Task CheckAsync()
    {
        // Nach dem Schließen des Dialogs ist die Abbruchquelle entsorgt; ein Zugriff
        // auf ihr Token würde werfen.
        if (_disposed)
        {
            return;
        }

        IsBusy = true;
        IsInstallAvailable = false;
        _asset = null;
        StatusText = UiText.Get("Update_Status_Checking");

        try
        {
            // force: Der Nutzer hat den Knopf gedrückt; die Drossel gilt nur für die
            // automatische Prüfung beim Start.
            ShowResult(await _checker.CheckForUpdateAsync(force: true, _cancellation.Token).ConfigureAwait(true));
        }
        catch (OperationCanceledException)
        {
            // Der Dialog wurde während der Prüfung geschlossen; die Antwort braucht niemand mehr.
            StatusText = UiText.Get("Update_Status_Cancelled");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowResult(UpdateCheckResult result)
    {
        StatusText = DescribeResult(result);
        if (result.IsInstallable)
        {
            _asset = result.Asset;
            IsInstallAvailable = true;
        }
    }

    private void StartVerified(UpdateDownloadResult download)
    {
        if (download is not { IsVerified: true, InstallerPath: { } installerPath })
        {
            StatusText = DescribeDownloadFailure(download.Status);
            return;
        }

        StatusText = UiText.Get("Update_Status_Verified");
        if (_installer.StartInstaller(installerPath))
        {
            InstallerStarted?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            StatusText = UiText.Format("Update_Status_InstallerNotStarted", installerPath);
        }
    }

    private async Task InstallAsync()
    {
        if (_disposed || _asset is null)
        {
            return;
        }

        IsBusy = true;
        IsProgressVisible = true;
        ProgressPercent = 0;
        StatusText = UiText.Get("Update_Status_Downloading");

        try
        {
            Progress<int> progress = new(p => ProgressPercent = p);
            StartVerified(await _installer.DownloadAndVerifyAsync(_asset, progress, _cancellation.Token).ConfigureAwait(true));
        }
        catch (OperationCanceledException)
        {
            // Der Dialog wurde während des Downloads geschlossen. Der Teil-Download ist
            // bereits verworfen; hier bleibt nichts zu tun.
            StatusText = UiText.Get("Update_Status_Cancelled");
        }
        finally
        {
            IsBusy = false;
            IsProgressVisible = false;
        }
    }
}
