using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
internal sealed partial class UpdateSectionViewModel : ObservableObject
{
    private readonly IUpdateChecker _checker;
    private readonly IUpdateInstaller _installer;

    [ObservableProperty]
    private string _statusText = "Noch nicht geprüft.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private bool _isProgressVisible;

    [ObservableProperty]
    private bool _isInstallAvailable;

    private UpdateAsset? _asset;

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

    /// <summary>Formuliert das Prüfergebnis als Satz für die Oberfläche.</summary>
    private static string DescribeResult(UpdateCheckResult result) => result.Status switch
    {
        UpdateCheckStatus.UpdateAvailable when result.IsInstallable =>
            $"Version {result.LatestVersion} ist verfügbar (installiert: {result.CurrentVersion}).",
        UpdateCheckStatus.UpdateAvailable =>
            $"Version {result.LatestVersion} ist verfügbar, lässt sich hier aber nicht installieren — "
            + "zu diesem Release fehlt die Prüfsumme. Bitte über die Release-Seite herunterladen.",
        UpdateCheckStatus.UpToDate => $"Die installierte Version {result.CurrentVersion} ist aktuell.",
        UpdateCheckStatus.Failed => "Es ließ sich nicht feststellen, ob es eine neuere Version gibt — "
            + "die Quelle war nicht erreichbar. Das heißt nicht, dass Sie aktuell sind.",
        _ => "Noch nicht geprüft.",
    };

    private static string DescribeDownloadFailure(UpdateDownloadStatus status) => status switch
    {
        UpdateDownloadStatus.DownloadFailed => "Der Download ist fehlgeschlagen. Bitte später erneut versuchen.",
        UpdateDownloadStatus.ChecksumMismatch =>
            "Die Prüfsumme der geladenen Datei stimmt nicht mit der veröffentlichten überein. "
            + "Die Datei wurde verworfen und nicht ausgeführt.",
        UpdateDownloadStatus.NoChecksumPublished =>
            "Zu diesem Release ist keine Prüfsumme veröffentlicht — es wird nicht automatisch installiert.",
        UpdateDownloadStatus.StorageFailed => "Das Paket ließ sich lokal nicht ablegen.",
        _ => "Die Installation ist fehlgeschlagen.",
    };

    partial void OnIsBusyChanged(bool value)
    {
        CheckCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsInstallAvailableChanged(bool value) => InstallCommand.NotifyCanExecuteChanged();

    private async Task CheckAsync()
    {
        IsBusy = true;
        IsInstallAvailable = false;
        _asset = null;
        StatusText = "Suche nach Updates …";

        try
        {
            // force: Der Nutzer hat den Knopf gedrückt — die Drossel gilt nur für die
            // automatische Prüfung beim Start.
            UpdateCheckResult result = await _checker
                .CheckForUpdateAsync(force: true, CancellationToken.None)
                .ConfigureAwait(true);

            StatusText = DescribeResult(result);
            if (result.IsInstallable)
            {
                _asset = result.Asset;
                IsInstallAvailable = true;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallAsync()
    {
        if (_asset is null)
        {
            return;
        }

        IsBusy = true;
        IsProgressVisible = true;
        ProgressPercent = 0;
        StatusText = "Lädt herunter …";

        try
        {
            Progress<int> progress = new(p => ProgressPercent = p);
            UpdateDownloadResult download = await _installer
                .DownloadAndVerifyAsync(_asset, progress, CancellationToken.None)
                .ConfigureAwait(true);

            if (!download.IsVerified)
            {
                StatusText = DescribeDownloadFailure(download.Status);
                return;
            }

            StatusText = "Prüfsumme bestätigt — Installation wird gestartet.";
            if (_installer.StartInstaller(download.InstallerPath!))
            {
                InstallerStarted?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                StatusText = "Das Installationsprogramm ließ sich nicht starten. "
                    + $"Die geprüfte Datei liegt unter {download.InstallerPath}.";
            }
        }
        finally
        {
            IsBusy = false;
            IsProgressVisible = false;
        }
    }
}
