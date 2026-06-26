using System.Windows;
using MdExplorer.App.ViewModels.Help;

namespace MdExplorer.App.Views.Help;

/// <summary>
/// „Über MdExplorer…"-Dialog. Modal, schließt sich über die Esc-Taste oder den
/// „Schließen"-Button. Bindet das <see cref="AboutViewModel"/> einmalig im
/// Konstruktor; ein Refresh zur Laufzeit ist nicht vorgesehen.
/// </summary>
internal sealed partial class AboutWindow : Window
{
    /// <summary>Erzeugt das Fenster und setzt den DataContext.</summary>
    public AboutWindow(AboutViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnCloseClick(object sender, RoutedEventArgs args) => Close();
}
