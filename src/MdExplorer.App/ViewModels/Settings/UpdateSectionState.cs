using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Localization;
using MdExplorer.Update.Abstractions;
using MdExplorer.Update.Models;

namespace MdExplorer.App.ViewModels.Settings;

/// <summary>Stationen des Update-Abschnitts.</summary>
internal enum UpdateSectionState
{
    /// <summary>Noch nicht geprüft.</summary>
    NotChecked,

    /// <summary>Die Prüfung läuft.</summary>
    Checking,

    /// <summary>Geprüft, nichts zu installieren (aktuell, nicht installierbar oder Prüfung gescheitert).</summary>
    Checked,

    /// <summary>Ein installierbares Paket liegt bereit.</summary>
    Available,

    /// <summary>Das Paket wird heruntergeladen und geprüft.</summary>
    Downloading,

    /// <summary>Herunterladen, Prüfen oder Starten ist gescheitert; ein neuer Versuch ist möglich.</summary>
    DownloadFailed,

    /// <summary>Das Installationsprogramm läuft; die Anwendung endet gleich.</summary>
    InstallerStarted,

    /// <summary>Abgebrochen, weil der Dialog geschlossen wurde.</summary>
    Cancelled,
}
