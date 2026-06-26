using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MdExplorer.App.ViewModels;
using MdExplorer.Core;
using MdExplorer.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace MdExplorer.App.Views.Panels;

/// <summary>
/// Rechtes Panel mit der WebView2-Vorschau. Initialisiert die CoreWebView2 lazy,
/// puffert den HTML-Inhalt bis Ready, fängt <c>mdexplorer://</c>-Navigationen ab und
/// leitet sie über den injizierten <see cref="INavigationService"/> weiter.
/// </summary>
internal sealed partial class PreviewPanel : UserControl
{
    /// <summary>URL-Schema für interne WikiLink-Navigation.</summary>
    public const string WikiLinkScheme = "mdexplorer:";

    private readonly ILogger<PreviewPanel> _logger;
    private readonly INavigationService _navigationService;
    private bool _isCoreReady;
    private string? _pendingHtml;

    /// <summary>Erstellt das Panel mit den Abhängigkeiten aus dem DI-Container.</summary>
    public PreviewPanel(INavigationService navigationService, ILogger<PreviewPanel> logger)
    {
        ArgumentNullException.ThrowIfNull(navigationService);
        ArgumentNullException.ThrowIfNull(logger);
        _navigationService = navigationService;
        _logger = logger;
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        try
        {
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: Path.Combine(AppPaths.GetApplicationDataDirectory(), "webview2")).ConfigureAwait(true);
            await Browser.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
            ConfigureCoreSettings();
            _isCoreReady = true;
            FlushPendingHtml();
        }
        catch (InvalidOperationException exception)
        {
            LogInitFailure(_logger, exception);
        }
        catch (FileNotFoundException exception)
        {
            LogInitFailure(_logger, exception);
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            LogRuntimeMissing(_logger, exception);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        DataContextChanged -= OnDataContextChanged;
        if (DataContext is PreviewViewModel previousViewModel)
        {
            previousViewModel.PropertyChanged -= OnPreviewViewModelChanged;
        }
        if (_isCoreReady)
        {
            Browser.CoreWebView2.NavigationStarting -= OnNavigationStarting;
        }
        Browser.Dispose();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.OldValue is PreviewViewModel previous)
        {
            previous.PropertyChanged -= OnPreviewViewModelChanged;
        }
        if (args.NewValue is PreviewViewModel current)
        {
            current.PropertyChanged += OnPreviewViewModelChanged;
            UpdateHtml(current.Html);
        }
    }

    private void OnPreviewViewModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!string.Equals(args.PropertyName, nameof(PreviewViewModel.Html), StringComparison.Ordinal))
        {
            return;
        }
        if (DataContext is PreviewViewModel viewModel)
        {
            UpdateHtml(viewModel.Html);
        }
    }

    private void UpdateHtml(string html)
    {
        if (!_isCoreReady)
        {
            _pendingHtml = html;
            return;
        }
        Browser.NavigateToString(html);
    }

    private void FlushPendingHtml()
    {
        if (_pendingHtml is null)
        {
            return;
        }
        string html = _pendingHtml;
        _pendingHtml = null;
        Browser.NavigateToString(html);
    }

    private void ConfigureCoreSettings()
    {
        CoreWebView2Settings settings = Browser.CoreWebView2.Settings;
        // Preview ist reines HTML+CSS — Skripte und postMessage sind weder noetig noch erwuenscht.
        settings.IsScriptEnabled = false;
        settings.IsWebMessageEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
#if DEBUG
        settings.AreDevToolsEnabled = true;
#else
        settings.AreDevToolsEnabled = false;
#endif
        Browser.CoreWebView2.NavigationStarting += OnNavigationStarting;
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!args.Uri.StartsWith(WikiLinkScheme, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        args.Cancel = true;
        string target = Uri.UnescapeDataString(args.Uri[WikiLinkScheme.Length..].TrimStart('/'));
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }
        _ = NavigateAsync(target);
    }

    private async Task NavigateAsync(string wikiLinkTarget)
    {
        try
        {
            _ = await _navigationService.NavigateToWikiLinkAsync(wikiLinkTarget, CancellationToken.None).ConfigureAwait(true);
        }
        catch (InvalidOperationException exception)
        {
            LogNavigationFailure(_logger, exception, wikiLinkTarget);
        }
    }

    [LoggerMessage(EventId = 410, Level = LogLevel.Error, Message = "WebView2-Initialisierung fehlgeschlagen.")]
    private static partial void LogInitFailure(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 411, Level = LogLevel.Error, Message = "WebView2-Runtime nicht installiert — Vorschau bleibt leer.")]
    private static partial void LogRuntimeMissing(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 412, Level = LogLevel.Error, Message = "WikiLink-Navigation für {WikiLinkTarget} fehlgeschlagen.")]
    private static partial void LogNavigationFailure(ILogger logger, Exception exception, string wikiLinkTarget);
}
