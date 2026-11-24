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

/// <summary>Ein wählbarer Änderungszeitraum.</summary>
internal sealed partial class PeriodFilterViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    /// <summary>Erzeugt den Umschalter für einen Zeitraum.</summary>
    public PeriodFilterViewModel(AllFilesPeriod period, string label)
    {
        Period = period;
        Label = label;
    }

    /// <summary>Der Zeitraum, für den dieser Umschalter steht.</summary>
    public AllFilesPeriod Period { get; }

    /// <summary>Die Aufschrift.</summary>
    public string Label { get; }
}
