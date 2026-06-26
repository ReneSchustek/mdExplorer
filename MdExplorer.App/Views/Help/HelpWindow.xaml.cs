using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MdExplorer.App.Services;
using MdExplorer.App.Services.Help;
using MdExplorer.App.ViewModels.Help;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Views.Help;

/// <summary>
/// Nicht-modales Hilfefenster mit Inhaltsverzeichnis links, Such-Zeile oben
/// und WebView2-Render-Bereich rechts. Position und Größe werden über
/// <see cref="HelpWindowGeometryStore"/> in der <c>ui-layout.json</c>
/// persistiert; das Hilfefenster bleibt ein Singleton pro Anwendungsstart
/// und nutzt <see cref="Show"/>/<see cref="Window.Activate"/>, wenn es bereits offen ist.
/// </summary>
internal sealed partial class HelpWindow : Window
{
    private const string CssPlaceholder = "__INLINE_CSS__";
    private const string JsPlaceholder = "__INLINE_JS__";
    private const string HtmlPlaceholder = "__HELP_HTML__";
    private const string NoncePlaceholder = "__SCRIPT_NONCE__";
    private const string ThemePlaceholder = "__THEME__";
    private const int NonceByteLength = 16;
    private const int SearchDebounceMs = 200;

    private readonly IHelpContentService _contentService;
    private readonly ISettingsService _settingsService;
    private readonly HelpWindowGeometryStore _geometryStore;
    private readonly HelpViewModel _viewModel;
    private readonly ILogger<HelpWindow> _logger;
    private readonly DispatcherTimer _searchDebounce;

    private HelpContent? _content;
    private bool _isWebViewReady;
    private string _pendingSlug = HelpContext.TableOfContents;
    private bool _suppressTocSelectionChanged;

    /// <summary>Erzeugt das Fenster mit allen abhängigen Services.</summary>
    public HelpWindow(
        IHelpContentService contentService,
        ISettingsService settingsService,
        HelpWindowGeometryStore geometryStore,
        ILogger<HelpWindow> logger)
    {
        ArgumentNullException.ThrowIfNull(contentService);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(geometryStore);
        ArgumentNullException.ThrowIfNull(logger);
        InitializeComponent();

        _contentService = contentService;
        _settingsService = settingsService;
        _geometryStore = geometryStore;
        _logger = logger;
        _viewModel = new HelpViewModel();
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

    private void ApplyPersistedGeometry()
    {
        WindowGeometry? geometry = _geometryStore.Load();
        if (geometry is null)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            return;
        }
        Left = geometry.Left;
        Top = geometry.Top;
        Width = Math.Max(MinWidth, geometry.Width);
        Height = Math.Max(MinHeight, geometry.Height);
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs args)
    {
        try
        {
            await HelpView.EnsureCoreWebView2Async().ConfigureAwait(true);
            _content = await _contentService.GetAsync(CancellationToken.None).ConfigureAwait(true);
            _viewModel.SetToc(_content.Toc);
            string html = BuildHtml(_content.Html, ResolveTheme());
            HelpView.NavigateToString(html);
            HelpView.CoreWebView2.DOMContentLoaded += OnDomContentLoaded;
        }
        catch (InvalidOperationException ex)
        {
            LogInitializationFailed(_logger, ex);
        }
    }

    private async void OnDomContentLoaded(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2DOMContentLoadedEventArgs args)
    {
        _isWebViewReady = true;
        // Erster Scroll-Versuch nach DOM-ready — vorher gibt es kein Layout, also kein Ziel.
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
        if (!_isWebViewReady)
        {
            return;
        }
        string query = SearchBox.Text ?? string.Empty;
        string script = $"window.MdExplorerHelp.highlight({JsonSerializer.Serialize(query)})";
        string raw = await HelpView.CoreWebView2.ExecuteScriptAsync(script).ConfigureAwait(true);
        UpdateHitLabel(raw, hitIndex: raw == "0" ? -1 : 0);
    }

    private async void OnNextHit(object sender, RoutedEventArgs args)
    {
        if (!_isWebViewReady) { return; }
        string indexRaw = await HelpView.CoreWebView2.ExecuteScriptAsync("window.MdExplorerHelp.next()").ConfigureAwait(true);
        string countRaw = await HelpView.CoreWebView2.ExecuteScriptAsync("window.MdExplorerHelp.hitCount()").ConfigureAwait(true);
        UpdateHitLabel(countRaw, ParseInt(indexRaw));
    }

    private async void OnPreviousHit(object sender, RoutedEventArgs args)
    {
        if (!_isWebViewReady) { return; }
        string indexRaw = await HelpView.CoreWebView2.ExecuteScriptAsync("window.MdExplorerHelp.previous()").ConfigureAwait(true);
        string countRaw = await HelpView.CoreWebView2.ExecuteScriptAsync("window.MdExplorerHelp.hitCount()").ConfigureAwait(true);
        UpdateHitLabel(countRaw, ParseInt(indexRaw));
    }

    private void UpdateHitLabel(string countRaw, int hitIndex)
    {
        int count = ParseInt(countRaw);
        if (count <= 0)
        {
            HitLabel.Text = "0 / 0";
            return;
        }
        int humanIndex = hitIndex < 0 ? 0 : hitIndex + 1;
        HitLabel.Text = string.Create(CultureInfo.InvariantCulture, $"{humanIndex} / {count}");
    }

    private static int ParseInt(string raw)
    {
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
    }

    private async Task ScrollToSlugAsync(string slug)
    {
        string script = $"window.MdExplorerHelp.scrollToSlug({JsonSerializer.Serialize(slug)})";
        _ = await HelpView.CoreWebView2.ExecuteScriptAsync(script).ConfigureAwait(true);
    }

    private async void OnTocSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_suppressTocSelectionChanged || !_isWebViewReady)
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
        HelpTocEntry? match = _viewModel.Toc
            .FirstOrDefault(entry => string.Equals(entry.Slug, slug, StringComparison.Ordinal));
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

    private string ResolveTheme()
    {
        AppTheme theme = _settingsService.Current.Appearance.Theme;
        return theme switch
        {
            AppTheme.Dark => "dark",
            AppTheme.Light => "light",
            _ => IsSystemDarkTheme() ? "dark" : "light",
        };
    }

#pragma warning disable S3400 // Stub-Methode: liefert vorerst konstant `false`, behaelt aber Methoden-Signatur als Erweiterungspunkt fuer kuenftige System-Theme-Erkennung.
    private static bool IsSystemDarkTheme()
    {
        // WPF kennt kein zentrales System-Theme-API; im Zweifel der hellere Default,
        // bis ein zukuenftiger UI-Polish-Brief die WinUI-Theme-Erkennung nachzieht.
        return false;
    }
#pragma warning restore S3400

    /// <summary>HTML-Template mit CSS, JS, Nonce, Theme und Handbuch-HTML füllen.</summary>
    internal static string BuildHtml(string helpHtml, string theme)
    {
        ArgumentNullException.ThrowIfNull(helpHtml);
        ArgumentException.ThrowIfNullOrWhiteSpace(theme);
        string htmlTemplate = ReadEmbeddedResource("help.html");
        string css = ReadEmbeddedResource("help.css");
        string js = ReadEmbeddedResource("help.js");
        string nonce = GenerateNonce();
        // Reihenfolge wie im GraphWindow: erst die vertrauenswuerdigen Platzhalter,
        // dann der Markdig-Output, damit der nicht versehentlich Platzhalter ersetzt.
        return htmlTemplate
            .Replace(CssPlaceholder, css, StringComparison.Ordinal)
            .Replace(JsPlaceholder, js, StringComparison.Ordinal)
            .Replace(NoncePlaceholder, nonce, StringComparison.Ordinal)
            .Replace(ThemePlaceholder, theme, StringComparison.Ordinal)
            .Replace(HtmlPlaceholder, helpHtml, StringComparison.Ordinal);
    }

    private static string GenerateNonce()
    {
        Span<byte> buffer = stackalloc byte[NonceByteLength];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer);
    }

    private static string ReadEmbeddedResource(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        Assembly assembly = typeof(HelpWindow).Assembly;
        string resourceName = "MdExplorer.App.Assets.Help." + fileName;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Eingebettete Hilfe-Ressource fehlt: {resourceName}.");
        }
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private void OnClosedHandler(object? sender, EventArgs args)
    {
        _searchDebounce.Stop();
        _searchDebounce.Tick -= OnSearchDebounceTick;
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

    [LoggerMessage(EventId = 1110, Level = LogLevel.Error, Message = "Hilfefenster konnte nicht initialisiert werden.")]
    private static partial void LogInitializationFailed(ILogger logger, Exception exception);
}
