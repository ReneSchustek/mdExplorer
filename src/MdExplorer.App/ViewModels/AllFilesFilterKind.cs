using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Localization;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Diagnostics;
using MdExplorer.Core.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>Woran ein Filter der Datei-Liste greift.</summary>
internal enum AllFilesFilterKind
{
    /// <summary>Der Ordner, in dem eine Datei liegt.</summary>
    Folder = 0,

    /// <summary>Eine Kennzeichnung der Datei.</summary>
    Tag = 1,

    /// <summary>Der Zeitraum der letzten Änderung.</summary>
    Period = 2,
}
