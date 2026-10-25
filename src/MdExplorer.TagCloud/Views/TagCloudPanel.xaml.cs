using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MdExplorer.TagCloud.Messaging;
using MdExplorer.TagCloud.ViewModels;

namespace MdExplorer.TagCloud.Views;

/// <summary>
/// UserControl der Tag-Cloud. Übersetzt UI-Mausklicks mit Modifier-Tasten in
/// <see cref="TagFilterMode"/>-Aufrufe am <see cref="TagCloudViewModel"/>.
/// Strg → Add, Alt → Exclude, sonst Replace. Das Kontextmenü bietet alle drei auch ohne Maus.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed partial class TagCloudPanel : UserControl
{
    /// <summary>Befehl hinter dem ✕ im Kopf; ohne ihn bleibt der Knopf verborgen.</summary>
    public static readonly DependencyProperty CloseCommandProperty = DependencyProperty.Register(
        nameof(CloseCommand),
        typeof(ICommand),
        typeof(TagCloudPanel),
        new PropertyMetadata(defaultValue: null, OnCloseCommandChanged));

    /// <summary>Erstellt das UserControl.</summary>
    public TagCloudPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Blendet die Tag-Cloud aus. Das Ein- und Ausblenden gehört dem Hauptfenster, das die Spalte
    /// besitzt; der Bereich selbst bietet nur den Knopf dafür an.
    /// </summary>
    public ICommand? CloseCommand
    {
        get => (ICommand?)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    private static void OnCloseCommandChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((TagCloudPanel)owner).CloseButton.Visibility = args.NewValue is null ? Visibility.Collapsed : Visibility.Visible;

    private static TagFilterMode DetermineMode()
    {
        ModifierKeys modifiers = Keyboard.Modifiers;

        // Alt vor Strg: Mit beiden Tasten gewinnt der Ausschluss.
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

    private void OnTagClicked(object sender, RoutedEventArgs args) => Handle(sender, DetermineMode());

    private void OnTagMenuClicked(object sender, RoutedEventArgs args)
    {
        if (sender is MenuItem { Tag: TagFilterMode mode })
        {
            Handle(sender, mode);
        }
    }

    // Menüeinträge erben den DataContext des Schlagworts über das Kontextmenü.
    private void Handle(object sender, TagFilterMode mode)
    {
        ArgumentNullException.ThrowIfNull(sender);
        if (sender is FrameworkElement { DataContext: TagItemViewModel item } && DataContext is TagCloudViewModel viewModel)
        {
            viewModel.HandleTagClicked(item, mode);
        }
    }
}
