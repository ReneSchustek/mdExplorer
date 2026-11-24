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

/// <summary>Zeiträume, auf die sich die Liste einschränken lässt.</summary>
internal enum AllFilesPeriod
{
    /// <summary>Ohne Einschränkung.</summary>
    Any = 0,

    /// <summary>Seit Mitternacht der Ortszeit geändert.</summary>
    Today = 1,

    /// <summary>In den letzten sieben Tagen geändert.</summary>
    LastSevenDays = 2,

    /// <summary>In den letzten dreißig Tagen geändert.</summary>
    LastThirtyDays = 3,
}
