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

/// <summary>Ein Buchstabe der Sprungleiste.</summary>
internal sealed partial class JumpLetterViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _hasEntries;

    /// <summary>Erzeugt den Eintrag für einen Buchstaben.</summary>
    public JumpLetterViewModel(char letter)
    {
        Letter = letter;
        Label = letter.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Der Buchstabe selbst.</summary>
    public char Letter { get; }

    /// <summary>Der Buchstabe als Text, für Anzeige und Befehlsparameter.</summary>
    public string Label { get; }
}
