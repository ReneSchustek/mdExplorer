using System.Windows;
using MdExplorer.App.Services.Help;
using MdExplorer.App.ViewModels.Settings;

namespace MdExplorer.App.Views.Settings;

/// <summary>
/// Settings-Fenster. Reagiert auf <see cref="SettingsWindowViewModel.CloseRequested"/>
/// und setzt darauf <see cref="Window.DialogResult"/>, damit der Aufrufer den
/// Save/Cancel-Pfad unterscheiden kann.
/// </summary>
internal sealed partial class SettingsWindow : Window
{
    private readonly SettingsWindowViewModel _viewModel;
    private readonly IHelpContextProvider _helpContextProvider;
    private string? _previousHelpSlug;

    /// <summary>Erzeugt das Fenster und bindet das ViewModel an den DataContext.</summary>
    public SettingsWindow(SettingsWindowViewModel viewModel, IHelpContextProvider helpContextProvider)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(helpContextProvider);
        InitializeComponent();
        _viewModel = viewModel;
        _helpContextProvider = helpContextProvider;
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        Loaded += OnLoadedHandler;
        Closed += OnWindowClosed;
    }

    private void OnLoadedHandler(object sender, RoutedEventArgs args)
    {
        _previousHelpSlug = _helpContextProvider.CurrentSlug;
        _helpContextProvider.SetSlug(HelpContext.Indexing);
    }

    private void OnCloseRequested(object? sender, SettingsCloseEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        DialogResult = args.SavedChanges;
        Close();
    }

    private void OnWindowClosed(object? sender, EventArgs args)
    {
        _viewModel.CloseRequested -= OnCloseRequested;
        Loaded -= OnLoadedHandler;
        Closed -= OnWindowClosed;
        // Modaler Dialog: nach dem Schliessen den vorherigen Kontext zurueckgeben,
        // damit F1 wieder dort weitermacht, wo der Anwender vor dem Dialog war.
        if (_previousHelpSlug is not null)
        {
            _helpContextProvider.SetSlug(_previousHelpSlug);
        }
    }
}
