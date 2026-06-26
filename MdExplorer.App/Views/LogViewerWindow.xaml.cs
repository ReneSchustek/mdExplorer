using System.Windows;
using System.Windows.Controls;
using MdExplorer.App.ViewModels;

namespace MdExplorer.App.Views;

/// <summary>
/// Live-Log-Viewer auf Basis des In-Memory-Sinks. Bindet das
/// <see cref="LogViewerViewModel"/> und schließt sich entweder
/// per Esc/Close-Button oder durch die Hauptanwendung.
/// </summary>
internal sealed partial class LogViewerWindow : Window
{
    private readonly LogViewerViewModel _viewModel;

    /// <summary>Erstellt das Fenster und setzt den DataContext.</summary>
    public LogViewerWindow(LogViewerViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Closed += OnWindowClosed;
    }

    private void OnCloseClick(object sender, RoutedEventArgs args) => Close();

    private void OnWindowClosed(object? sender, EventArgs args)
    {
        Closed -= OnWindowClosed;
        _viewModel.Dispose();
    }

    /// <summary>
    /// Versteht <see cref="GridViewColumnHeader"/>-Klicks als „keine Aktion" — das
    /// Routing-Event bubble-up käme sonst beim DockPanel an und würde im
    /// Snoop-Tool als unbehandeltes Routed-Event auffallen.
    /// </summary>
    private void OnHeaderClick(object sender, RoutedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        args.Handled = true;
    }
}
