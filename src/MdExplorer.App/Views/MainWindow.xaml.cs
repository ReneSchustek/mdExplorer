using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Localization;
using MdExplorer.App.Services;
using MdExplorer.App.Services.Help;
using MdExplorer.App.ViewModels;
using MdExplorer.App.Views.Panels;
using MdExplorer.TagCloud.Views;

namespace MdExplorer.App.Views;

/// <summary>
/// Hauptfenster der Anwendung. Bindet das <see cref="MainViewModel"/>, setzt Dokument-Panel,
/// Dateiliste und Tag-Cloud in ihre Plätze, stellt das gespeicherte Layout her und speichert es
/// beim Schließen. Die Nebenfenster öffnet <see cref="SecondaryWindows"/>; hier wird nur der
/// Hilfe-Kontext nachgehalten.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed partial class MainWindow : Window
{
    // Dieselben Werte wie die Spaltenangaben in MainWindow.xaml. Die Breite gilt, solange die
    // Tag-Cloud noch nie ausgeblendet war und es keine gemerkte Breite gibt.
    private const double DefaultTagCloudColumnWidth = 260.0;
    private const double DefaultTagCloudSplitterWidth = 6.0;

    /// <summary>Schmalste sinnvolle Breite der Tag-Cloud, solange sie sichtbar ist.</summary>
    private const double DefaultTagCloudMinWidth = 180.0;

    /// <summary>So viele Zeichen des Ordners zeigt ein Eintrag im Verlauf.</summary>
    private const int MaxFolderLength = 48;

    private readonly MainViewModel _viewModel;
    private readonly SecondaryWindows _windows;
    private readonly IHelpContextProvider _helpContextProvider;
    private readonly IExternalLinkLauncher _linkLauncher;
    private double _restoredTagCloudColumnWidth = DefaultTagCloudColumnWidth;

    // Die Breite, die der Nutzer der linken Spalte gegeben hat. Ein schmales Fenster zeigt sie
    // schmaler an (ColumnFit), gespeichert wird aber diese.
    private double _wantedFolderColumnWidth;
    private bool _allFilesInitialized;
    private bool _closeConfirmed;

    /// <summary>Erstellt das Hauptfenster.</summary>
    public MainWindow(
        MainViewModel viewModel,
        MainWindowPanels panels,
        SecondaryWindowFactories windowFactories,
        IHelpContextProvider helpContextProvider,
        IExternalLinkLauncher linkLauncher)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(windowFactories);
        ArgumentNullException.ThrowIfNull(helpContextProvider);
        ArgumentNullException.ThrowIfNull(linkLauncher);
        InitializeComponent();
        _viewModel = viewModel;
        _windows = new SecondaryWindows(this, windowFactories);
        _helpContextProvider = helpContextProvider;
        _linkLauncher = linkLauncher;
        DataContext = viewModel;

        DocumentHost.Content = panels.Document;
        AllFilesHost.Content = panels.AllFiles;
        TagCloudHost.Content = panels.TagCloud;
        panels.TagCloud.CloseCommand = _viewModel.HideTagCloudCommand;
        ApplyInitialLayout();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _ = InputBindings.Add(new KeyBinding(new RelayCommand(FocusSearch), Key.F, ModifierKeys.Control));
        _ = InputBindings.Add(new KeyBinding(new RelayCommand(_windows.ShowSettings), Key.OemComma, ModifierKeys.Control));
        _ = InputBindings.Add(new KeyBinding(new RelayCommand(OnHelpHotkey), Key.F1, ModifierKeys.None));
        _ = InputBindings.Add(new KeyBinding(_viewModel.ToggleTagCloudCommand, Key.T, ModifierKeys.Control));
        _ = InputBindings.Add(new KeyBinding(new RelayCommand(ShowRecentFiles), Key.R, ModifierKeys.Control));
        PreviewGotKeyboardFocus += OnPreviewGotKeyboardFocus;
        SizeChanged += OnSizeChanged;
    }

    private static MenuItem CreateRecentFileEntry(RecentFileItem item, RecentFilesViewModel recent)
    {
        // Ein TextBlock statt eines bloßen Texts als Kopf: Ein Unterstrich im Dateinamen würde
        // sonst als Zugriffstaste gelesen und verschwände.
        MenuItem entry = new()
        {
            Header = new TextBlock { Text = item.Exists ? item.Name : UiText.Format("RecentFiles_Missing_Format", item.Name) },
            InputGestureText = ShortenFolder(item.Folder),
            ToolTip = item.Exists ? item.FullPath : UiText.Format("RecentFiles_Missing_ToolTip", item.FullPath),
            Command = recent.OpenCommand,
            CommandParameter = item,
        };
        AutomationProperties.SetHelpText(entry, item.FullPath);
        if (!item.Exists)
        {
            entry.SetResourceReference(ForegroundProperty, "TextDisabledBrush");
        }
        return entry;
    }

    // Lange Ordner würden das Menü über den Bildschirm ziehen; das Ende des Pfads trägt die
    // Unterscheidung, der volle Pfad steht im Tooltip.
    private static string ShortenFolder(string folder) =>
        folder.Length <= MaxFolderLength ? folder : "…" + folder[^MaxFolderLength..];

    private static string ResolveContextSlug(DependencyObject focused)
    {
        DependencyObject? cursor = focused;
        while (cursor is not null)
        {
            switch (cursor)
            {
                // Das Kapitel heißt „Suche und Vorschau“; es gilt für beide Bereiche.
                case SearchPanel:
                case DocumentPanel:
                    return HelpContext.Search;
                case TagCloudPanel:
                    return HelpContext.TagCloud;
                case FolderTreePanel:
                    return HelpContext.Indexing;
                case AllFilesPanel:
                    return HelpContext.Layout;
            }
            cursor = VisualTreeHelper.GetParent(cursor) ?? LogicalTreeHelper.GetParent(cursor);
        }
        return HelpContext.TableOfContents;
    }

    private void OnPreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.NewFocus is not DependencyObject focused)
        {
            return;
        }
        string slug = ResolveContextSlug(focused);
        _helpContextProvider.SetSlug(slug);
    }

    private async void OnLeftTabsSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        // SelectionChanged steigt auch aus den Listen in den Registern auf; hier zählt nur der
        // Wechsel des Registers selbst.
        if (!ReferenceEquals(args.OriginalSource, LeftTabs))
        {
            return;
        }
        // Die Liste aller Dateien lädt erst, wenn ihr Register zum ersten Mal aufgeht. Die Marke
        // steht vor dem Laden, damit schnelles Hin- und Herschalten nicht doppelt lädt. Scheitert
        // das Laden, zeigt die Liste das selbst samt „Neu laden“; RefreshAsync wirft dafür nicht.
        if (!_allFilesInitialized && ReferenceEquals(LeftTabs.SelectedItem, AllFilesTabItem))
        {
            _allFilesInitialized = true;
            await _viewModel.AllFiles.RefreshAsync().ConfigureAwait(true);
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs args) => _windows.ShowSettings();

    private void OnRecentFilesClick(object sender, RoutedEventArgs args) => ShowRecentFiles();

    /// <summary>
    /// Klappt den Verlauf unter seinem Knopf auf (Klick oder Strg+R).
    /// </summary>
    /// <remarks>
    /// Das Menü entsteht bei jedem Aufklappen neu: Vorher wird jeder Eintrag auf der Platte geprüft,
    /// damit eine inzwischen gelöschte Datei als solche erscheint. Die Einträge rufen nur die Befehle
    /// des <see cref="RecentFilesViewModel"/>; entschieden wird dort.
    /// </remarks>
    private void ShowRecentFiles()
    {
        RecentFilesViewModel recent = _viewModel.RecentFiles;
        recent.Refresh();

        ContextMenu menu = new() { PlacementTarget = RecentFilesButton, Placement = PlacementMode.Bottom };
        if (!recent.HasItems)
        {
            _ = menu.Items.Add(new MenuItem { Header = UiText.Get("RecentFiles_Empty"), IsEnabled = false });
        }
        foreach (RecentFileItem item in recent.Items)
        {
            _ = menu.Items.Add(CreateRecentFileEntry(item, recent));
        }
        _ = menu.Items.Add(new Separator());
        _ = menu.Items.Add(new MenuItem { Header = UiText.Get("RecentFiles_Clear"), Command = recent.ClearCommand });
        menu.IsOpen = true;
    }

    private void OnGraphClick(object sender, RoutedEventArgs args) => _windows.ShowGraph();

    private void OnTagManagementClick(object sender, RoutedEventArgs args) => _windows.ShowTagManagement();

    // Die Skriptaufrufe im Hilfefenster fangen ihre Fehler selbst ab; hier kommt keiner an.
    private async void OnHelpClick(object sender, RoutedEventArgs args)
        => await _windows.ShowHelpAsync(HelpContext.TableOfContents).ConfigureAwait(true);

    private void OnAboutClick(object sender, RoutedEventArgs args) => _windows.ShowAbout();

    private void OnLogViewerClick(object sender, RoutedEventArgs args) => _windows.ShowLogViewer();

    private void OnHealthIndicatorClick(object sender, RoutedEventArgs args) => _windows.ShowLogViewer();

    // Die Adresse stammt aus der Antwort des Update-Servers. Der Öffner lässt nur http, https und
    // mailto zur Shell durch und protokolliert, was er abweist oder nicht starten kann.
    private void OnUpdateLinkRequestNavigate(object sender, RequestNavigateEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        args.Handled = true;
        _linkLauncher.Open(args.Uri);
    }

    private async void OnHelpHotkey() =>
        await _windows.ShowHelpAsync(_helpContextProvider.CurrentSlug).ConfigureAwait(true);

    private void ApplyInitialLayout()
    {
        _wantedFolderColumnWidth = _viewModel.FolderColumnWidth;
        FolderColumn.Width = new GridLength(_wantedFolderColumnWidth, GridUnitType.Pixel);
        // Die Vorschau ist die einzige Sternspalte. Der gespeicherte Wert wirkt nur als Gewicht,
        // die Spalte füllt in jedem Fall den Rest.
        PreviewColumn.Width = new GridLength(_viewModel.PreviewColumnWidth, GridUnitType.Star);
        ApplyTagCloudVisibility();
    }

    /// <summary>
    /// Hält das Schließen an, solange der Editor ungespeicherte Änderungen hat, und fragt nach.
    /// </summary>
    /// <remarks>
    /// Gefragt wird erst nach dem Closing-Ereignis: Ein <c>Close</c> aus dem laufenden Ereignis
    /// heraus lehnt WPF ab. Dürfen die Änderungen gehen, schließt das Fenster ein zweites Mal,
    /// dann ohne Frage.
    /// </remarks>
    private void OnClosing(object? sender, CancelEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (_closeConfirmed || !_viewModel.HasUnsavedChanges)
        {
            return;
        }
        args.Cancel = true;
        _ = Dispatcher.InvokeAsync(ConfirmAndCloseAsync);
    }

    private async Task ConfirmAndCloseAsync()
    {
        if (!await _viewModel.ConfirmCloseAsync().ConfigureAwait(true))
        {
            return;
        }
        _closeConfirmed = true;
        Close();
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        PreviewGotKeyboardFocus -= OnPreviewGotKeyboardFocus;
        SizeChanged -= OnSizeChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.RememberColumnWidths(_wantedFolderColumnWidth, PreviewColumn.ActualWidth);
        _viewModel.Dispose();
    }

    /// <summary>Strg+F-Pfad: schaltet auf den Such-Tab und fokussiert das Suchfeld.</summary>
    private void FocusSearch()
    {
        _viewModel.ShowSearchTab();
        // TabControl wechselt den ContentPresenter; Focus muss nach dem Binding-Roundtrip kommen.
        _ = Dispatcher.BeginInvoke(SearchPanelControl.FocusQueryBox, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (string.Equals(args.PropertyName, nameof(MainViewModel.IsTagCloudShown), StringComparison.Ordinal))
        {
            ApplyTagCloudVisibility();
        }
    }

    // Die Breite entscheidet das ViewModel; hier wird sie nur gemeldet.
    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.WidthChanged)
        {
            _viewModel.UpdateWindowWidth(args.NewSize.Width);
        }
    }

    // Nur der Trenner ändert die Wunschbreite, und jeder nur die seiner Spalte; das Anpassen an
    // das Fenster nicht. Sonst würde im schmalen Fenster die gestauchte Breite der anderen Spalte
    // zum Wunsch und beim Schließen gespeichert. Mit Pfeiltasten bewegt, meldet der Trenner kein
    // DragCompleted; deshalb auch beim Loslassen einer Taste übernehmen.
    private void OnFolderSplitterDragCompleted(object sender, DragCompletedEventArgs args) => TakeFolderColumnWidth();

    private void OnFolderSplitterKeyUp(object sender, KeyEventArgs args) => TakeFolderColumnWidth();

    private void OnTagCloudSplitterDragCompleted(object sender, DragCompletedEventArgs args) => TakeTagCloudColumnWidth();

    private void OnTagCloudSplitterKeyUp(object sender, KeyEventArgs args) => TakeTagCloudColumnWidth();

    private void TakeFolderColumnWidth() => _wantedFolderColumnWidth = FolderColumn.ActualWidth;

    private void TakeTagCloudColumnWidth()
    {
        if (TagCloudColumn.ActualWidth > 0.0)
        {
            _restoredTagCloudColumnWidth = TagCloudColumn.ActualWidth;
        }
    }

    private void OnColumnsSizeChanged(object sender, SizeChangedEventArgs args) => FitColumnsToWindow();

    private void FitColumnsToWindow()
    {
        bool tagCloudShown = _viewModel.IsTagCloudShown;
        double reserved = FolderSplitterColumn.ActualWidth + PreviewColumn.MinWidth
            + (tagCloudShown ? DefaultTagCloudSplitterWidth : 0.0);
        ColumnWidths widths = ColumnFit.Fit(
            ColumnsGrid.ActualWidth,
            reserved,
            new ColumnWish(_wantedFolderColumnWidth, FolderColumn.MinWidth),
            new ColumnWish(tagCloudShown ? _restoredTagCloudColumnWidth : 0.0, DefaultTagCloudMinWidth));
        FolderColumn.Width = new GridLength(widths.Folder, GridUnitType.Pixel);
        if (tagCloudShown)
        {
            TagCloudColumn.Width = new GridLength(widths.TagCloud, GridUnitType.Pixel);
        }
    }

    private void ApplyTagCloudVisibility()
    {
        bool visible = _viewModel.IsTagCloudShown;
        TagCloudSplitterColumn.Width = visible
            ? new GridLength(DefaultTagCloudSplitterWidth, GridUnitType.Pixel)
            : new GridLength(0.0);
        TagCloudColumn.Width = visible
            ? new GridLength(_restoredTagCloudColumnWidth, GridUnitType.Pixel)
            : new GridLength(0.0);
        // Die Mindestbreite schlägt die Breite: Ohne diese Zeile bliebe die Spalte trotz
        // Breite 0 stehen, als 180 Einheiten breiter leerer Streifen am rechten Rand, der im
        // dunklen Erscheinungsbild hell aufleuchtet.
        TagCloudColumn.MinWidth = visible ? DefaultTagCloudMinWidth : 0.0;
        TagCloudSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        TagCloudHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        FitColumnsToWindow();
    }
}
