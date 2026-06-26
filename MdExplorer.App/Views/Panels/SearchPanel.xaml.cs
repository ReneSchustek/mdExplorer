using System.Windows.Controls;
using System.Windows.Input;
using MdExplorer.App.ViewModels;

namespace MdExplorer.App.Views.Panels;

/// <summary>
/// Mittleres Panel mit Suchfeld und Trefferliste. Behandelt die Tastatur-Shortcuts
/// <see cref="Key.Escape"/> (Eingabe löschen) und <see cref="Key.Enter"/> (Treffer öffnen).
/// </summary>
internal sealed partial class SearchPanel : UserControl
{
    /// <summary>Erstellt das Panel.</summary>
    public SearchPanel()
    {
        InitializeComponent();
    }

    /// <summary>Fokussiert das Suchfeld — wird vom MainWindow-Shortcut Strg+F gerufen.</summary>
    public void FocusQueryBox()
    {
        _ = QueryTextBox.Focus();
        QueryTextBox.SelectAll();
    }

    private void OnQueryKeyDown(object sender, KeyEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (DataContext is not SearchViewModel viewModel)
        {
            return;
        }
        if (args.Key == Key.Escape)
        {
            viewModel.Clear();
            args.Handled = true;
        }
    }
}
