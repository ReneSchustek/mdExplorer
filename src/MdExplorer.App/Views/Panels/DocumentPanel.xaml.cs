using System.Diagnostics.CodeAnalysis;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Localization;
using MdExplorer.App.Services;
using MdExplorer.App.ViewModels;

namespace MdExplorer.App.Views.Panels;

/// <summary>
/// Rechte Spalte: im Lesemodus die <see cref="PreviewPanel"/>-Instanz, im Bearbeitungsmodus das
/// Textfeld des Editors. Wann welcher Modus gilt, entscheidet das
/// <see cref="DocumentPanelViewModel"/>; hier stehen nur der Aufbau, die Suchleiste des Editors
/// und die Tastenkürzel Strg+E, Strg+S, Strg+F und F3.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed partial class DocumentPanel : UserControl
{
    private readonly PreviewPanel _previewPanel;

    /// <summary>Erstellt das Panel und hängt das WebView2-Preview-Control als Read-Mode-Inhalt ein.</summary>
    public DocumentPanel(PreviewPanel previewPanel)
    {
        ArgumentNullException.ThrowIfNull(previewPanel);
        _previewPanel = previewPanel;
        InitializeComponent();
        PreviewHost.Content = _previewPanel;
        DataContextChanged += OnDataContextChanged;
        _ = InputBindings.Add(new KeyBinding(new RelayCommand(ToggleMode, () => ViewModel is not null), Key.E, ModifierKeys.Control));
        _ = InputBindings.Add(new KeyBinding(new RelayCommand(Save, () => ViewModel is not null), Key.S, ModifierKeys.Control));
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

    /// <summary>Öffnet im Bearbeitungsmodus die Suchleiste und übernimmt eine markierte Stelle als
    /// Suchbegriff. Im Lesemodus greift die Bindung nicht, und Strg+F erreicht die Suche des
    /// Hauptfensters.</summary>
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
            UpdateFindStatus(UiText.Get("Document_Find_NoHit"));
            return;
        }
        EditorTextBox.Select(hit.StartIndex, hit.Length);
        // Select allein rollt nicht. ScrollToLine braucht die Zeilennummer, nicht den Zeichenindex.
        int line = source.AsSpan(0, hit.StartIndex).Count('\n');
        EditorTextBox.ScrollToLine(line);
        // Für Menschen ab eins gezählt; ScrollToLine zählt ab null.
        UpdateFindStatus(UiText.Format("Document_Find_HitAtLine", line + 1));
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

    // Über den Befehl der Schaltfläche, nicht SaveAsync direkt: Er lässt keinen zweiten
    // Speichervorgang zu, solange einer läuft. Mehrfaches Strg+S speicherte sonst parallel.
    private void Save()
    {
        if (ViewModel?.SaveCommand is { } save && save.CanExecute(CancellationToken.None))
        {
            save.Execute(CancellationToken.None);
        }
    }
}
