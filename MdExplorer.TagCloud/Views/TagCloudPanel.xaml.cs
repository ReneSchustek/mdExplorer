using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MdExplorer.TagCloud.Messaging;
using MdExplorer.TagCloud.ViewModels;

namespace MdExplorer.TagCloud.Views;

/// <summary>
/// UserControl der Tag-Cloud. Übersetzt UI-Mausklicks mit Modifier-Tasten in
/// <see cref="TagFilterMode"/>-Aufrufe am <see cref="TagCloudViewModel"/>.
/// Strg → Add, Alt → Exclude, sonst Replace.
/// </summary>
public sealed partial class TagCloudPanel : UserControl
{
    /// <summary>Erstellt das UserControl.</summary>
    public TagCloudPanel()
    {
        InitializeComponent();
    }

    private void OnTagClicked(object sender, RoutedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(sender);
        if (sender is not FrameworkElement element)
        {
            return;
        }
        if (element.DataContext is not TagItemViewModel item)
        {
            return;
        }
        if (DataContext is not TagCloudViewModel viewModel)
        {
            return;
        }
        TagFilterMode mode = DetermineMode();
        viewModel.HandleTagClicked(item, mode);
    }

    private static TagFilterMode DetermineMode()
    {
        ModifierKeys modifiers = Keyboard.Modifiers;
        if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
        {
            return TagFilterMode.Exclude;
        }
        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            return TagFilterMode.Add;
        }
        return TagFilterMode.Replace;
    }
}
