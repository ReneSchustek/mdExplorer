using System.Windows;
using System.Windows.Controls;
using MdExplorer.App.ViewModels;

namespace MdExplorer.App.Views.Panels;

/// <summary>
/// Linkes Panel mit der baumartigen Ordner-Navigation. Code-Behind ist auf das
/// reine Event-Wiring beschränkt — Auswahl wird ans <see cref="FolderTreeViewModel"/> gespiegelt.
/// </summary>
internal sealed partial class FolderTreePanel : UserControl
{
    /// <summary>Erstellt das Panel.</summary>
    public FolderTreePanel()
    {
        InitializeComponent();
    }

    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (DataContext is FolderTreeViewModel viewModel)
        {
            viewModel.SelectedNode = args.NewValue as TreeNodeViewModel;
        }
    }
}
