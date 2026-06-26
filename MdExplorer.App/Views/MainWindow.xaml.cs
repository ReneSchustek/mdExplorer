using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Services.Help;
using MdExplorer.App.ViewModels;
using MdExplorer.App.Views.Graph;
using MdExplorer.App.Views.Help;
using MdExplorer.App.Views.Panels;
using MdExplorer.App.Views.Settings;
using MdExplorer.TagCloud.Views;

namespace MdExplorer.App.Views;

/// <summary>
/// Hauptfenster der Anwendung. Bindet das <see cref="MainViewModel"/>, hängt den
/// Preview-Host, das Suchpanel und das Tag-Cloud-Panel auf, übernimmt Initial-Layout
/// und persistiert es beim Schließen.
/// </summary>
internal sealed partial class MainWindow : Window
{
    private const double DefaultTagCloudColumnWidth = 260.0;
    private const double DefaultTagCloudSplitterWidth = 6.0;
    private const int SearchTabIndex = 2;

    private readonly MainViewModel _viewModel;
    private readonly Func<SettingsWindow> _settingsWindowFactory;
    private readonly Func<GraphWindow> _graphWindowFactory;
    private readonly Func<MdExplorer.TagCloud.Views.TagManagementWindow> _tagManagementWindowFactory;
    private readonly Func<HelpWindow> _helpWindowFactory;
    private readonly Func<AboutWindow> _aboutWindowFactory;
    private readonly Func<LogViewerWindow> _logViewerWindowFactory;
    private readonly IHelpContextProvider _helpContextProvider;
    private HelpWindow? _activeHelpWindow;
    private LogViewerWindow? _activeLogViewerWindow;
    private double _restoredTagCloudColumnWidth = DefaultTagCloudColumnWidth;
    private bool _allFilesInitialized;

    /// <summary>Erstellt das Hauptfenster.</summary>
    public MainWindow(
        MainViewModel viewModel,
        DocumentPanel documentPanel,
        AllFilesPanel allFilesPanel,
        TagCloudPanel tagCloudPanel,
        Func<SettingsWindow> settingsWindowFactory,
        Func<GraphWindow> graphWindowFactory,
        Func<MdExplorer.TagCloud.Views.TagManagementWindow> tagManagementWindowFactory,
        Func<HelpWindow> helpWindowFactory,
        Func<AboutWindow> aboutWindowFactory,
        Func<LogViewerWindow> logViewerWindowFactory,
        IHelpContextProvider helpContextProvider)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(documentPanel);
        ArgumentNullException.ThrowIfNull(allFilesPanel);
        ArgumentNullException.ThrowIfNull(tagCloudPanel);
        ArgumentNullException.ThrowIfNull(settingsWindowFactory);
        ArgumentNullException.ThrowIfNull(graphWindowFactory);
        ArgumentNullException.ThrowIfNull(tagManagementWindowFactory);
        ArgumentNullException.ThrowIfNull(helpWindowFactory);
        ArgumentNullException.ThrowIfNull(aboutWindowFactory);
        ArgumentNullException.ThrowIfNull(logViewerWindowFactory);
        ArgumentNullException.ThrowIfNull(helpContextProvider);
        InitializeComponent();
        _viewModel = viewModel;
        _settingsWindowFactory = settingsWindowFactory;
        _graphWindowFactory = graphWindowFactory;
        _tagManagementWindowFactory = tagManagementWindowFactory;
        _helpWindowFactory = helpWindowFactory;
        _aboutWindowFactory = aboutWindowFactory;
        _logViewerWindowFactory = logViewerWindowFactory;
        _helpContextProvider = helpContextProvider;
        DataContext = viewModel;

        DocumentHost.Content = documentPanel;
        AllFilesHost.Content = allFilesPanel;
        TagCloudHost.Content = tagCloudPanel;
        ApplyInitialLayout();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _ = InputBindings.Add(new KeyBinding(new RelayCommand(FocusSearch), Key.F, ModifierKeys.Control));
        _ = InputBindings.Add(new KeyBinding(new RelayCommand(ShowSettingsWindow), Key.OemComma, ModifierKeys.Control));
        _ = InputBindings.Add(new KeyBinding(new RelayCommand(OnHelpHotkey), Key.F1, ModifierKeys.None));
        _ = InputBindings.Add(new KeyBinding(_viewModel.ToggleTagCloudCommand, Key.T, ModifierKeys.Control));
        PreviewGotKeyboardFocus += OnPreviewGotKeyboardFocus;
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

    private static string ResolveContextSlug(DependencyObject focused)
    {
        DependencyObject? cursor = focused;
        while (cursor is not null)
        {
            switch (cursor)
            {
                case SearchPanel:
                    return HelpContext.Search;
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

    private async void OnLeftTabsSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!ReferenceEquals(args.OriginalSource, LeftTabs))
        {
            return;
        }
        if (!_allFilesInitialized && ReferenceEquals(LeftTabs.SelectedItem, AllFilesTabItem))
        {
            _allFilesInitialized = true;
            await _viewModel.AllFiles.RefreshAsync().ConfigureAwait(true);
        }
    }

    private void OnSettingsMenuItemClick(object sender, RoutedEventArgs args) => ShowSettingsWindow();

    private void OnGraphMenuItemClick(object sender, RoutedEventArgs args) => ShowGraphWindow();

    private void OnTagManagementMenuItemClick(object sender, RoutedEventArgs args) => ShowTagManagementWindow();

    private async void OnHelpMenuItemClick(object sender, RoutedEventArgs args)
        => await ShowHelpWindowAsync(HelpContext.TableOfContents).ConfigureAwait(true);

    private void OnAboutMenuItemClick(object sender, RoutedEventArgs args) => ShowAboutWindow();

    private void OnLogViewerMenuItemClick(object sender, RoutedEventArgs args) => ShowLogViewerWindow();

    private void OnHealthIndicatorClick(object sender, RoutedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        ShowLogViewerWindow();
        args.Handled = true;
    }

    private void OnUpdateLinkRequestNavigate(object sender, RequestNavigateEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        args.Handled = true;
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or System.IO.FileNotFoundException)
        {
            // Kein Standardbrowser oder Start verweigert — der Hinweis bleibt sichtbar,
            // der Nutzer kann die URL manuell öffnen. Bewusst nicht-fatal.
        }
    }

    private void ShowSettingsWindow()
    {
        SettingsWindow window = _settingsWindowFactory();
        window.Owner = this;
        _ = window.ShowDialog();
    }

    private void ShowGraphWindow()
    {
        GraphWindow window = _graphWindowFactory();
        window.Owner = this;
        window.Show();
    }

    private void ShowTagManagementWindow()
    {
        MdExplorer.TagCloud.Views.TagManagementWindow window = _tagManagementWindowFactory();
        window.Owner = this;
        _ = window.ShowDialog();
    }

    private async Task ShowHelpWindowAsync(string slug)
    {
        // Wenn das Fenster bereits offen ist, holen wir es nur in den Vordergrund
        // und navigieren zum gewünschten Slug — ein zweites Fenster waere verwirrend.
        if (_activeHelpWindow is not null)
        {
            if (_activeHelpWindow.WindowState == WindowState.Minimized)
            {
                _activeHelpWindow.WindowState = WindowState.Normal;
            }
            _ = _activeHelpWindow.Activate();
            await _activeHelpWindow.NavigateToAsync(slug).ConfigureAwait(true);
            return;
        }
        HelpWindow window = _helpWindowFactory();
        window.Owner = this;
        window.Closed += OnHelpWindowClosed;
        _activeHelpWindow = window;
        window.Show();
        await window.NavigateToAsync(slug).ConfigureAwait(true);
    }

    private void OnHelpWindowClosed(object? sender, EventArgs args)
    {
        if (sender is HelpWindow window)
        {
            window.Closed -= OnHelpWindowClosed;
        }
        _activeHelpWindow = null;
    }

    private void ShowAboutWindow()
    {
        AboutWindow window = _aboutWindowFactory();
        window.Owner = this;
        _ = window.ShowDialog();
    }

    private void ShowLogViewerWindow()
    {
        if (_activeLogViewerWindow is not null)
        {
            if (_activeLogViewerWindow.WindowState == WindowState.Minimized)
            {
                _activeLogViewerWindow.WindowState = WindowState.Normal;
            }
            _ = _activeLogViewerWindow.Activate();
            return;
        }
        LogViewerWindow window = _logViewerWindowFactory();
        window.Owner = this;
        window.Closed += OnLogViewerWindowClosed;
        _activeLogViewerWindow = window;
        window.Show();
    }

    private void OnLogViewerWindowClosed(object? sender, EventArgs args)
    {
        if (sender is LogViewerWindow window)
        {
            window.Closed -= OnLogViewerWindowClosed;
        }
        _activeLogViewerWindow = null;
    }

    private async void OnHelpHotkey()
    {
        string slug = _helpContextProvider.CurrentSlug;
        await ShowHelpWindowAsync(slug).ConfigureAwait(true);
    }

    private void ApplyInitialLayout()
    {
        FolderColumn.Width = new GridLength(_viewModel.FolderColumnWidth, GridUnitType.Pixel);
        PreviewColumn.Width = new GridLength(_viewModel.PreviewColumnWidth, GridUnitType.Star);
        ApplyTagCloudVisibility();
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        PreviewGotKeyboardFocus -= OnPreviewGotKeyboardFocus;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.FolderColumnWidth = FolderColumn.ActualWidth;
        _viewModel.PreviewColumnWidth = PreviewColumn.ActualWidth;
        _viewModel.PersistColumnLayout();
        _viewModel.Dispose();
    }

    /// <summary>Strg+F-Pfad — schaltet auf den Such-Tab und fokussiert das Suchfeld.</summary>
    private void FocusSearch()
    {
        _viewModel.LeftTabIndex = SearchTabIndex;
        // TabControl wechselt den ContentPresenter — Focus muss nach dem Binding-Roundtrip kommen.
        _ = Dispatcher.BeginInvoke(SearchPanelControl.FocusQueryBox, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (string.Equals(args.PropertyName, nameof(MainViewModel.IsTagCloudVisible), StringComparison.Ordinal))
        {
            ApplyTagCloudVisibility();
        }
    }

    private void ApplyTagCloudVisibility()
    {
        bool visible = _viewModel.IsTagCloudVisible;
        if (!visible && TagCloudColumn.ActualWidth > 0.0)
        {
            _restoredTagCloudColumnWidth = TagCloudColumn.ActualWidth;
        }
        TagCloudSplitterColumn.Width = visible
            ? new GridLength(DefaultTagCloudSplitterWidth, GridUnitType.Pixel)
            : new GridLength(0.0);
        TagCloudColumn.Width = visible
            ? new GridLength(_restoredTagCloudColumnWidth, GridUnitType.Pixel)
            : new GridLength(0.0);
        TagCloudSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        TagCloudHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

}
