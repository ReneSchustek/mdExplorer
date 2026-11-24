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

/// <summary>Ein Filter, der gerade wirkt, als entfernbarer Chip dargestellt.</summary>
internal sealed class ActiveFilterViewModel
{
    /// <summary>Erzeugt den Chip zu einem wirkenden Filter.</summary>
    public ActiveFilterViewModel(AllFilesFilterKind kind, string value, string label)
    {
        Kind = kind;
        Value = value;
        Label = label;
    }

    /// <summary>Woran der Filter greift.</summary>
    public AllFilesFilterKind Kind { get; }

    /// <summary>Der eingestellte Wert: Ordner, Kennzeichnung oder Zeitraum.</summary>
    public string Value { get; }

    /// <summary>Die Aufschrift des Chips.</summary>
    public string Label { get; }

    /// <summary>Beschriftung der Entfernen-Schaltfläche, auch für die Sprachausgabe.</summary>
    public string RemoveHint => UiText.Format("AllFiles_Filter_Remove", Label);
}
