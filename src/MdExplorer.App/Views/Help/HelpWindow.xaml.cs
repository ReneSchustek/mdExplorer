using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MdExplorer.App.Services;
using MdExplorer.App.Services.Help;
using MdExplorer.App.ViewModels.Help;
using MdExplorer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Views.Help;

/// <summary>
/// Nicht-modales Hilfefenster mit Inhaltsverzeichnis links, Such-Zeile oben
/// und WebView2-Render-Bereich rechts. Position und Größe werden über
/// <see cref="HelpWindowGeometryStore"/> in der <c>ui-layout.json</c>
/// persistiert. Es gibt höchstens eines je Anwendung: Ist es schon offen, holt das
/// Hauptfenster es nach vorn, statt ein zweites zu erzeugen.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed partial class HelpWindow : Window
{
    // Wartezeit nach dem letzten Tastenanschlag, bevor die Treffer im Handbuch neu markiert werden.
    private const int SearchDebounceMs = 200;

    private readonly IHelpContentService _contentService;
    private readonly IEffectiveThemeProvider _themeProvider;
    private readonly HelpWindowGeometryStore _geometryStore;
    private readonly HelpViewModel _viewModel;
    private readonly IExternalLinkLauncher _linkLauncher;
    private readonly ILogger<HelpWindow> _logger;
    private readonly DispatcherTimer _searchDebounce;

    private EmbeddedNavigationGuard? _navigationGuard;
    private bool _isWebViewReady;
    private string _pendingSlug = HelpContext.TableOfContents;
    private bool _suppressTocSelectionChanged;

    /// <summary>Erzeugt das Fenster mit allen abhängigen Services.</summary>
    public HelpWindow(
        HelpViewModel viewModel,
        IHelpContentService contentService,
        IEffectiveThemeProvider themeProvider,
        HelpWindowGeometryStore geometryStore,
        IExternalLinkLauncher linkLauncher,
        ILogger<HelpWindow> logger)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(contentService);
        ArgumentNullException.ThrowIfNull(themeProvider);
        ArgumentNullException.ThrowIfNull(geometryStore);
        ArgumentNullException.ThrowIfNull(linkLauncher);
        ArgumentNullException.ThrowIfNull(logger);
        InitializeComponent();

        _viewModel = viewModel;
        _contentService = contentService;
        _themeProvider = themeProvider;
        _geometryStore = geometryStore;
        _linkLauncher = linkLauncher;
        _logger = logger;
        DataContext = _viewModel;
        TocList.ItemsSource = _viewModel.Toc;

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SearchDebounceMs) };
        _searchDebounce.Tick += OnSearchDebounceTick;

        ApplyPersistedGeometry();
        Loaded += OnLoadedAsync;
        Closed += OnClosedHandler;
    }

    /// <summary>Setzt den Ziel-Anker und scrollt im WebView2 zur passenden Überschrift.</summary>
    public async Task NavigateToAsync(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        _pendingSlug = slug;
        if (_isWebViewReady)
        {
            await ScrollToSlugAsync(slug).ConfigureAwait(true);
            SelectTocEntry(slug);
        }
    }

    private static int ParseScriptNumber(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;

    [LoggerMessage(EventId = 1110, Level = LogLevel.Error, Message = "Handbuch ließ sich nicht laden.")]
    private static partial void LogContentFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1111, Level = LogLevel.Warning, Message = "Skriptaufruf im Hilfefenster gescheitert.")]
    private static partial void LogScriptFailed(ILogger logger, Exception exception);

    // Nach dem Abstecken eines Bildschirms läge die gemerkte Lage womöglich außerhalb jeder
    // sichtbaren Fläche; dann öffnet das Fenster mittig über dem Hauptfenster.
    private void ApplyPersistedGeometry()
    {
        WindowGeometry? saved = _geometryStore.Load();
        Rect screen = new(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        WindowGeometry? geometry = saved is null ? null : WindowPlacement.FitOnScreen(saved, screen, MinWidth, MinHeight);
        if (geometry is null)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            return;
        }
        Left = geometry.Left;
        Top = geometry.Top;
        Width = geometry.Width;
        Height = geometry.Height;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs args)
    {
        if (!await EmbeddedBrowser.TryStartAsync(HelpView, UnavailableHint, _logger).ConfigureAwait(true))
        {
            return;
        }
        EmbeddedBrowser.Restrict(HelpView.CoreWebView2, allowScripts: true);
        _navigationGuard = new EmbeddedNavigationGuard(HelpView.CoreWebView2, PreviewNavigationPolicy.IsStringDocument, _linkLauncher, _logger);

        try
        {
            HelpContent content = await _contentService.GetAsync(CancellationToken.None).ConfigureAwait(true);
            _viewModel.SetToc(content.Toc);
            HelpView.CoreWebView2.DOMContentLoaded += OnDomContentLoaded;
            HelpView.NavigateToString(HelpDocumentBuilder.Build(content.Html, _themeProvider.IsDarkMode));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            // Fehlt das eingebettete Handbuch oder lässt es sich nicht lesen, bleibt die Fläche
            // leer; der Grund steht im Protokoll.
            LogContentFailed(_logger, exception);
        }
    }

    private async void OnDomContentLoaded(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2DOMContentLoadedEventArgs args)
    {
        _isWebViewReady = true;
        // Erster Scroll-Versuch nach DOM-ready, denn vorher gibt es kein Layout, also kein Ziel.
        if (!string.IsNullOrEmpty(_pendingSlug))
        {
            await ScrollToSlugAsync(_pendingSlug).ConfigureAwait(true);
            SelectTocEntry(_pendingSlug);
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs args)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private async void OnSearchDebounceTick(object? sender, EventArgs args)
    {
        _searchDebounce.Stop();
        string query = SearchBox.Text ?? string.Empty;
        string? count = await RunScriptAsync($"window.MdExplorerHelp.highlight({JsonSerializer.Serialize(query)})").ConfigureAwait(true);
        if (count is not null)
        {
            _viewModel.ShowHits(ParseScriptNumber(count), index: 0);
        }
    }

    private async void OnNextHit(object sender, RoutedEventArgs args) =>
        await MoveToHitAsync("window.MdExplorerHelp.next()").ConfigureAwait(true);

    private async void OnPreviousHit(object sender, RoutedEventArgs args) =>
        await MoveToHitAsync("window.MdExplorerHelp.previous()").ConfigureAwait(true);

    private async Task MoveToHitAsync(string script)
    {
        string? index = await RunScriptAsync(script).ConfigureAwait(true);
        string? count = await RunScriptAsync("window.MdExplorerHelp.hitCount()").ConfigureAwait(true);
        if (index is not null && count is not null)
        {
            _viewModel.ShowHits(ParseScriptNumber(count), ParseScriptNumber(index));
        }
    }

    private async Task ScrollToSlugAsync(string slug) =>
        _ = await RunScriptAsync($"window.MdExplorerHelp.scrollToSlug({JsonSerializer.Serialize(slug)})").ConfigureAwait(true);

    /// <summary>
    /// Führt ein Skript im Handbuch aus. Liefert <see langword="null"/>, wenn die Fläche nicht
    /// bereit ist oder der Aufruf scheitert.
    /// </summary>
    /// <remarks>
    /// Die Aufrufer sind Ereignis-Handler. Schließt der Nutzer das Fenster, während ein Aufruf
    /// läuft, ist die Fläche danach freigegeben; die Ausnahme darf nicht im allgemeinen
    /// Fehlerdialog landen.
    /// </remarks>
    private async Task<string?> RunScriptAsync(string script)
    {
        if (!_isWebViewReady)
        {
            return null;
        }
        try
        {
            return await HelpView.CoreWebView2.ExecuteScriptAsync(script).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException or COMException)
        {
            LogScriptFailed(_logger, exception);
            return null;
        }
    }

    private async void OnTocSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_suppressTocSelectionChanged)
        {
            return;
        }
        if (TocList.SelectedItem is HelpTocEntry entry)
        {
            await ScrollToSlugAsync(entry.Slug).ConfigureAwait(true);
        }
    }

    private void SelectTocEntry(string slug)
    {
        HelpTocEntry? match = _viewModel.FindEntry(slug);
        if (match is null)
        {
            return;
        }
        _suppressTocSelectionChanged = true;
        try
        {
            TocList.SelectedItem = match;
            TocList.ScrollIntoView(match);
        }
        finally
        {
            _suppressTocSelectionChanged = false;
        }
    }

    private void OnClosedHandler(object? sender, EventArgs args)
    {
        _isWebViewReady = false;
        _searchDebounce.Stop();
        _searchDebounce.Tick -= OnSearchDebounceTick;
        _navigationGuard?.Detach();
        if (HelpView.CoreWebView2 is not null)
        {
            HelpView.CoreWebView2.DOMContentLoaded -= OnDomContentLoaded;
        }
        if (WindowState == WindowState.Normal)
        {
            _geometryStore.Save(new WindowGeometry(Left, Top, Width, Height));
        }
        HelpView.Dispose();
        Loaded -= OnLoadedAsync;
        Closed -= OnClosedHandler;
    }
}
