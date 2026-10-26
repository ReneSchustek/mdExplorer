using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MdExplorer.App.Services;
using MdExplorer.App.ViewModels;
using MdExplorer.Core;
using MdExplorer.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace MdExplorer.App.Views.Panels;

/// <summary>
/// Rechtes Panel mit der WebView2-Vorschau. Initialisiert die CoreWebView2 lazy und puffert
/// den HTML-Inhalt bis Ready. Jede Navigation aus dem Dokument entscheidet
/// <see cref="PreviewNavigationPolicy"/>: WikiLinks gehen an den <see cref="INavigationService"/>,
/// Verweise ins Netz an den Standardbrowser, alles andere wird verworfen.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed partial class PreviewPanel : UserControl
{
    private readonly ILogger<PreviewPanel> _logger;
    private readonly INavigationService _navigationService;
    private readonly IExternalLinkLauncher _linkLauncher;
    private readonly OwnNavigations _ownNavigations = new();
    private bool _isCoreReady;
    private string? _pendingHtml;

    /// <summary>Erstellt das Panel mit den Abhängigkeiten aus dem DI-Container.</summary>
    public PreviewPanel(INavigationService navigationService, IExternalLinkLauncher linkLauncher, ILogger<PreviewPanel> logger)
    {
        ArgumentNullException.ThrowIfNull(navigationService);
        ArgumentNullException.ThrowIfNull(linkLauncher);
        ArgumentNullException.ThrowIfNull(logger);
        _navigationService = navigationService;
        _linkLauncher = linkLauncher;
        _logger = logger;
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    [LoggerMessage(EventId = 412, Level = LogLevel.Error, Message = "WikiLink-Navigation für {WikiLinkTarget} fehlgeschlagen.")]
    private static partial void LogNavigationFailure(ILogger logger, Exception exception, string wikiLinkTarget);

    [LoggerMessage(EventId = 413, Level = LogLevel.Debug, Message = "Navigation aus der Vorschau verworfen ({Kind}).")]
    private static partial void LogNavigationBlocked(ILogger logger, PreviewNavigationKind kind);

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (!await EmbeddedBrowser.TryStartAsync(Browser, UnavailableHint, _logger).ConfigureAwait(true))
        {
            return;
        }
        ConfigureCoreSettings();
        _isCoreReady = true;
        FlushPendingHtml();
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
            Browser.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
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
        ShowHtml(html);
    }

    // Die Ordnerabbildung gehört zu jedem Laden dazu, auch zum ersten aus dem Puffer: Ohne sie
    // fehlen dem Dokument die Bilder, bis Datei oder Theme wechseln.
    private void ShowHtml(string html)
    {
        MapDocumentFolder();
        _ownNavigations.Expect();
        Browser.NavigateToString(html);
    }

    /// <summary>
    /// Bildet den Bildordner des angezeigten Dokuments (für einen Index-Eintrag seine Wurzel) auf
    /// den virtuellen Ordner der Vorschau ab.
    /// </summary>
    /// <remarks>
    /// Nur lesend und ohne fremde Herkunft (<c>DenyCors</c>): Die Vorschau soll Bilder
    /// anzeigen, nicht mit dem Dateisystem sprechen. Eine frühere Abbildung wird jedes Mal
    /// gelöst, sonst zeigte das nächste Dokument die Bilder des vorigen.
    /// </remarks>
    private void MapDocumentFolder()
    {
        try
        {
            Browser.CoreWebView2.ClearVirtualHostNameToFolderMapping(PreviewHtmlBuilder.ImageHost);
        }
        catch (ArgumentException)
        {
            // Es gab noch keine Abbildung, also kein Grund, etwas zu melden.
        }

        if (DataContext is not PreviewViewModel { ImageBase: { } imageBase }
            || string.IsNullOrWhiteSpace(imageBase.Folder)
            || !Directory.Exists(imageBase.Folder))
        {
            return;
        }

        Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
            PreviewHtmlBuilder.ImageHost,
            imageBase.Folder,
            CoreWebView2HostResourceAccessKind.DenyCors);
    }

    private void FlushPendingHtml()
    {
        if (_pendingHtml is null)
        {
            return;
        }
        string html = _pendingHtml;
        _pendingHtml = null;
        ShowHtml(html);
    }

    private void ConfigureCoreSettings()
    {
        // Die Vorschau ist reines HTML und CSS; Skripte sind weder nötig noch erwünscht.
        EmbeddedBrowser.Restrict(Browser.CoreWebView2, allowScripts: false);
        Browser.CoreWebView2.NavigationStarting += OnNavigationStarting;
        Browser.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        PreviewNavigationKind kind = PreviewNavigationPolicy.Classify(args.Uri, args.IsUserInitiated, _ownNavigations.TryConsume);
        if (kind == PreviewNavigationKind.Document)
        {
            return;
        }
        args.Cancel = true;
        Follow(kind, args.Uri);
    }

    // Ein Verweis mit target="_blank" fragt nach einem neuen Fenster. Ohne Behandlung öffnete
    // WebView2 ein eigenes Fenster mit der fremden Seite, an jeder Prüfung vorbei.
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        args.Handled = true;
        Follow(PreviewNavigationPolicy.Classify(args.Uri, args.IsUserInitiated), args.Uri);
    }

    private void Follow(PreviewNavigationKind kind, string uri)
    {
        switch (kind)
        {
            case PreviewNavigationKind.WikiLink when PreviewNavigationPolicy.WikiLinkTarget(uri) is { } target:
                _ = NavigateAsync(target);
                break;
            case PreviewNavigationKind.External:
                _linkLauncher.Open(new Uri(uri, UriKind.Absolute));
                break;
            default:
                LogNavigationBlocked(_logger, kind);
                break;
        }
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
}
