using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using MdExplorer.App.ViewModels;

namespace MdExplorer.App.Views;

/// <summary>
/// Live-Log-Viewer auf Basis des In-Memory-Sinks. Bindet das
/// <see cref="LogViewerViewModel"/>; freigegeben wird es mit dem Scope des Fensters. Esc löst
/// über <c>IsCancel</c> denselben Schließen-Knopf aus.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed partial class LogViewerWindow : Window
{
    /// <summary>Erstellt das Fenster und setzt den DataContext.</summary>
    public LogViewerWindow(LogViewerViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnCloseClick(object sender, RoutedEventArgs args) => Close();

    /// <summary>
    /// Die Spaltenköpfe sortieren nicht. Ein Klick auf einen <see cref="GridViewColumnHeader"/>
    /// endet hier, statt unbehandelt durch den Baum aufzusteigen.
    /// </summary>
    private void OnHeaderClick(object sender, RoutedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        args.Handled = true;
    }
}
