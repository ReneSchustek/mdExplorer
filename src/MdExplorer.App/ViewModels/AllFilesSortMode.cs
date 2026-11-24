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

/// <summary>Sortier-Modi für die Datei-Liste.</summary>
internal enum AllFilesSortMode
{
    /// <summary>Standard: nach Schreibdatum absteigend.</summary>
    LastModified = 0,

    /// <summary>Nach Dateiname (Titel) aufsteigend.</summary>
    Title = 1,

    /// <summary>Nach relativem Pfad aufsteigend.</summary>
    RelativePath = 2,
}
