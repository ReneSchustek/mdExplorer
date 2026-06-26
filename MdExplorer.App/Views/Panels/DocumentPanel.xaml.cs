using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Services;
using MdExplorer.App.ViewModels;

namespace MdExplorer.App.Views.Panels;

/// <summary>
/// Container-Panel der rechten Spalte. Bettet die <see cref="PreviewPanel"/>-Instanz fuer den
/// Read-Modus ein und blendet im Edit-Modus die monospace TextBox des Editors ein. Die
/// Mode-Logik liegt vollstaendig im <see cref="DocumentPanelViewModel"/> — der Code-Behind
/// kuemmert sich nur um den View-Aufbau und Tastenkuerzel (Ctrl+E / Ctrl+S / Ctrl+F).
/// </summary>
internal sealed partial class DocumentPanel : UserControl
{
    private readonly PreviewPanel _previewPanel;

    /// <summary>Erstellt das Panel und haengt das WebView2-Preview-Control als Read-Mode-Inhalt ein.</summary>
    public DocumentPanel(PreviewPanel previewPanel)
    {
        ArgumentNullException.ThrowIfNull(previewPanel);
        _previewPanel = previewPanel;
        InitializeComponent();
        PreviewHost.Content = _previewPanel;
        DataContextChanged += OnDataContextChanged;
        _ = InputBindings.Add(new KeyBinding(new RelayCommand(ToggleMode, () => ViewModel is not null), Key.E, ModifierKeys.Control));
        _ = InputBindings.Add(new KeyBinding(new RelayCommand(SaveAndForget, () => ViewModel is not null), Key.S, ModifierKeys.Control));
        _ = InputBindings.Add(new KeyBinding(new RelayCommand(OpenFind, IsEditMode), Key.F, ModifierKeys.Control));
        _ = InputBindings.Add(new KeyBinding(new RelayCommand(FindNext, IsEditMode), Key.F3, ModifierKeys.None));
        _ = InputBindings.Add(new KeyBinding(new RelayCommand(FindPrevious, IsEditMode), Key.F3, ModifierKeys.Shift));
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is DocumentPanelViewModel viewModel)
        {
            _previewPanel.DataContext = viewModel.Preview;
        }
    }

    private DocumentPanelViewModel? ViewModel => DataContext as DocumentPanelViewModel;

    /// <summary>Oeffnet die Find-Bar im Edit-Modus, fokussiert das Eingabefeld und
    /// uebernimmt die aktuelle Selektion als Voreinstellung. Im Read-Modus uebernimmt das
    /// WebView2 die Find-Funktionalitaet selbst (Default-Browser-Accelerator).</summary>
    private void OpenFind()
    {
        if (ViewModel is null || !ViewModel.IsEditMode)
        {
            return;
        }
        FindBar.Visibility = Visibility.Visible;
        if (EditorTextBox.SelectionLength > 0)
        {
            FindInput.Text = EditorTextBox.SelectedText;
        }
        FindInput.SelectAll();
        _ = FindInput.Focus();
        UpdateFindStatus(null);
    }

    private void CloseFind()
    {
        FindBar.Visibility = Visibility.Collapsed;
        FindStatus.Text = string.Empty;
        if (ViewModel?.IsEditMode == true)
        {
            _ = EditorTextBox.Focus();
        }
    }

    private void FindNext()
    {
        if (ViewModel is null || !ViewModel.IsEditMode)
        {
            return;
        }
        if (FindBar.Visibility != Visibility.Visible)
        {
            OpenFind();
            return;
        }
        string query = FindInput.Text;
        string source = EditorTextBox.Text ?? string.Empty;
        int searchStart = EditorTextBox.SelectionStart + EditorTextBox.SelectionLength;
        EditorFindHelper.FindMatch? match = EditorFindHelper.FindNext(source, query, searchStart);
        ApplyMatch(match, source, query);
    }

    private void FindPrevious()
    {
        if (ViewModel is null || !ViewModel.IsEditMode)
        {
            return;
        }
        if (FindBar.Visibility != Visibility.Visible)
        {
            OpenFind();
            return;
        }
        string query = FindInput.Text;
        string source = EditorTextBox.Text ?? string.Empty;
        EditorFindHelper.FindMatch? match = EditorFindHelper.FindPrevious(source, query, EditorTextBox.SelectionStart);
        ApplyMatch(match, source, query);
    }

    private void ApplyMatch(EditorFindHelper.FindMatch? match, string source, string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            UpdateFindStatus(null);
            return;
        }
        if (match is not { } hit)
        {
            UpdateFindStatus("Kein Treffer");
            return;
        }
        EditorTextBox.Select(hit.StartIndex, hit.Length);
        // Sicherstellen, dass der Treffer ins Sichtfeld scrollt — TextBox.ScrollToLine erwartet eine Zeile.
        int line = source.AsSpan(0, hit.StartIndex).Count('\n');
        EditorTextBox.ScrollToLine(line);
        UpdateFindStatus($"Treffer bei {hit.StartIndex}");
    }

    private void UpdateFindStatus(string? status) => FindStatus.Text = status ?? string.Empty;

    private void OnFindInputKeyDown(object sender, KeyEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Key == Key.Escape)
        {
            CloseFind();
            args.Handled = true;
            return;
        }
        if (args.Key == Key.Enter)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                FindPrevious();
            }
            else
            {
                FindNext();
            }
            args.Handled = true;
        }
    }

    private void OnFindTextChanged(object sender, TextChangedEventArgs args) => UpdateFindStatus(null);

    private void OnFindNextClick(object sender, RoutedEventArgs args) => FindNext();

    private void OnFindPreviousClick(object sender, RoutedEventArgs args) => FindPrevious();

    private void OnFindCloseClick(object sender, RoutedEventArgs args) => CloseFind();

    private bool IsEditMode() => ViewModel?.IsEditMode == true;

    private void ToggleMode() => ViewModel?.ToggleModeCommand.Execute(null);

    private void SaveAndForget() => _ = ViewModel?.SaveAsync(CancellationToken.None);
}
