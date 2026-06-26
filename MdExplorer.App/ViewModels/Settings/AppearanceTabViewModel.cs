using CommunityToolkit.Mvvm.ComponentModel;
using MdExplorer.Core.Models;

namespace MdExplorer.App.ViewModels.Settings;

/// <summary>
/// ViewModel für den Tab „Darstellung" — Theme, Preview-Schriftgröße,
/// Anzahl der Suchtreffer pro Seite. Gibt die <see cref="AppearanceSettings"/>
/// zurück, wenn der Anwender bestätigt.
/// </summary>
internal sealed partial class AppearanceTabViewModel : ObservableObject
{
    [ObservableProperty]
    private AppTheme _theme;

    [ObservableProperty]
    private int _previewFontSize;

    [ObservableProperty]
    private int _resultsPerPage;

    /// <summary>Erzeugt das ViewModel mit den aktuellen Settings.</summary>
    public AppearanceTabViewModel(AppearanceSettings initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        _theme = initial.Theme;
        _previewFontSize = initial.PreviewFontSize;
        _resultsPerPage = initial.ResultsPerPage;
    }

    /// <summary>Liste der Theme-Optionen für ComboBox-Binding.</summary>
    public static IReadOnlyList<AppTheme> AvailableThemes { get; } =
    [
        AppTheme.System,
        AppTheme.Light,
        AppTheme.Dark,
    ];

    /// <summary>Erzeugt das Settings-Record aus den aktuellen Eingaben.</summary>
    public AppearanceSettings ToSettings() => new(Theme, PreviewFontSize, ResultsPerPage);
}
