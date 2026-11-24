using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Localization;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Core.Settings;
using MdExplorer.Update.Abstractions;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels.Settings;

/// <summary>Eventdaten zum Schließen des Settings-Fensters.</summary>
internal sealed class SettingsCloseEventArgs : EventArgs
{
    /// <summary>Erzeugt das Event.</summary>
    public SettingsCloseEventArgs(bool savedChanges) => SavedChanges = savedChanges;

    /// <summary><see langword="true"/>, wenn die Änderungen persistiert wurden.</summary>
    public bool SavedChanges { get; }
}
