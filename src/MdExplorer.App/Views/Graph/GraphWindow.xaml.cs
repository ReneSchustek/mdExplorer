using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using MdExplorer.App.Services;
using MdExplorer.App.Services.Help;
using MdExplorer.App.ViewModels.Graph;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Views.Graph;

/// <summary>
/// WebView2-basiertes Graph-Fenster. Setzt das Dokument aus den eingebetteten Vorlagen und dem
/// aktuellen Snapshot zusammen (<see cref="GraphDocumentBuilder"/>) und lädt es über die eigene
/// Datei des Fensters (<see cref="GraphSnapshotFile"/>).
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed partial class GraphWindow : Window
{
    private readonly GraphViewModel _viewModel;
    private readonly IHelpContextProvider _helpContextProvider;
    private readonly IEffectiveThemeProvider _themeProvider;
    private readonly GraphSnapshotFile _snapshotFile;
    private readonly IExternalLinkLauncher _linkLauncher;
    private readonly ILogger<GraphWindow> _logger;
    private EmbeddedNavigationGuard? _navigationGuard;
    private bool _isInitialized;

    /// <summary>Erzeugt das Fenster und verdrahtet das ViewModel.</summary>
    public GraphWindow(
        GraphViewModel viewModel,
        IHelpContextProvider helpContextProvider,
        IEffectiveThemeProvider themeProvider,
        GraphSnapshotFile snapshotFile,
        IExternalLinkLauncher linkLauncher,
        ILogger<GraphWindow> logger)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(helpContextProvider);
        ArgumentNullException.ThrowIfNull(themeProvider);
        ArgumentNullException.ThrowIfNull(snapshotFile);
        ArgumentNullException.ThrowIfNull(linkLauncher);
        ArgumentNullException.ThrowIfNull(logger);
        InitializeComponent();
        _viewModel = viewModel;
        _helpContextProvider = helpContextProvider;
        _themeProvider = themeProvider;
        _snapshotFile = snapshotFile;
        _linkLauncher = linkLauncher;
        _logger = logger;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoadedAsync;
        Activated += OnActivatedHandler;
        Closed += OnWindowClosed;
    }

    [LoggerMessage(EventId = 1510, Level = LogLevel.Error, Message = "Graph-Dokument ließ sich nicht schreiben: {Path}.")]
    private static partial void LogSnapshotWriteFailed(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1511, Level = LogLevel.Warning, Message = "Graph-Dokument ließ sich nicht löschen: {Path}.")]
    private static partial void LogSnapshotDeleteFailed(ILogger logger, Exception exception, string path);

    private void OnActivatedHandler(object? sender, EventArgs args) =>
        _helpContextProvider.SetSlug(HelpContext.Graph);

    // Ein Fehler beim Start der Browserfläche darf das Fenster nicht abreißen: Es bleibt leer
    // stehen, ein Hinweis nennt den Grund. Fehler beim Laden des Graphen behandelt RefreshAsync
    // selbst.
    private async void OnLoadedAsync(object sender, RoutedEventArgs args)
    {
        if (!await EmbeddedBrowser.TryStartAsync(GraphView, UnavailableHint, _logger).ConfigureAwait(true))
        {
            return;
        }
        EmbeddedBrowser.Restrict(GraphView.CoreWebView2, allowScripts: true);
        _navigationGuard = new EmbeddedNavigationGuard(GraphView.CoreWebView2, _snapshotFile.IsThisDocument, _linkLauncher, _logger);

        _isInitialized = true;
        await _viewModel.RefreshAsync().ConfigureAwait(true);
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!_isInitialized || !string.Equals(args.PropertyName, nameof(GraphViewModel.SnapshotJson), StringComparison.Ordinal))
        {
            return;
        }
        string? json = _viewModel.SnapshotJson;
        if (string.IsNullOrEmpty(json))
        {
            return;
        }
        await ShowAsync(GraphDocumentBuilder.Build(json, _themeProvider.IsDarkMode)).ConfigureAwait(true);
    }

    private async Task ShowAsync(string html)
    {
        try
        {
            Uri document = await _snapshotFile.WriteAsync(html, CancellationToken.None).ConfigureAwait(true);
            if (!_isInitialized)
            {
                // Während des Schreibens geschlossen; die Fläche ist schon freigegeben.
                return;
            }
            // Navigate lädt auch dann neu, wenn sich nur der Inhalt geändert hat; ein Setzen von
            // Source mit gleicher Adresse übergingen WebView2 und der Zwischenspeicher.
            GraphView.CoreWebView2.Navigate(document.AbsoluteUri);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogSnapshotWriteFailed(_logger, exception, _snapshotFile.FilePath);
        }
    }

    private void OnWindowClosed(object? sender, EventArgs args)
    {
        _isInitialized = false;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Loaded -= OnLoadedAsync;
        Activated -= OnActivatedHandler;
        Closed -= OnWindowClosed;
        _navigationGuard?.Detach();
        GraphView.Dispose();
        DeleteSnapshot();
    }

    // Erst nach dem Freigeben der Fläche: Solange sie die Datei geöffnet hat, ließe sie sich
    // nicht löschen.
    private void DeleteSnapshot()
    {
        try
        {
            _snapshotFile.Delete();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogSnapshotDeleteFailed(_logger, exception, _snapshotFile.FilePath);
        }
    }
}
