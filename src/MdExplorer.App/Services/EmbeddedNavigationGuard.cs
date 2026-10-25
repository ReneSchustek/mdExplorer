using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace MdExplorer.App.Services;

/// <summary>
/// Hält eine eingebettete Fläche bei ihrem eigenen Dokument. Verweise ins Netz gehen an den
/// Standardbrowser, alles andere wird verworfen.
/// </summary>
/// <remarks>
/// Ohne Wächter führte ein Verweis im Handbuch die Hilfe ins Netz: ohne Adressleiste, ohne
/// Rückweg und an der Prüfung vorbei, die die Vorschau vornimmt. Die Entscheidung trifft
/// <see cref="PreviewNavigationPolicy"/>, dieselbe Regel wie in der Vorschau.
/// </remarks>
[ExcludeFromCodeCoverage]
internal sealed partial class EmbeddedNavigationGuard
{
    private readonly CoreWebView2 _core;
    private readonly Func<string, bool> _isOwnDocument;
    private readonly IExternalLinkLauncher _linkLauncher;
    private readonly ILogger _logger;

    /// <summary>Hängt den Wächter an die Fläche.</summary>
    /// <param name="core">Die gestartete Fläche.</param>
    /// <param name="isOwnDocument">Erkennt das Dokument, das die Anwendung selbst lädt.</param>
    /// <param name="linkLauncher">Öffnet Verweise ins Netz im Standardprogramm.</param>
    /// <param name="logger">Protokoll des Fensters.</param>
    public EmbeddedNavigationGuard(CoreWebView2 core, Func<string, bool> isOwnDocument, IExternalLinkLauncher linkLauncher, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(isOwnDocument);
        ArgumentNullException.ThrowIfNull(linkLauncher);
        ArgumentNullException.ThrowIfNull(logger);

        _core = core;
        _isOwnDocument = isOwnDocument;
        _linkLauncher = linkLauncher;
        _logger = logger;
        _core.NavigationStarting += OnNavigationStarting;
        _core.NewWindowRequested += OnNewWindowRequested;
    }

    /// <summary>Löst den Wächter von der Fläche, bevor das Fenster sie freigibt.</summary>
    public void Detach()
    {
        _core.NavigationStarting -= OnNavigationStarting;
        _core.NewWindowRequested -= OnNewWindowRequested;
    }

    [LoggerMessage(EventId = 422, Level = LogLevel.Debug, Message = "Navigation aus einer eingebetteten Fläche verworfen ({Kind}).")]
    private static partial void LogNavigationBlocked(ILogger logger, PreviewNavigationKind kind);

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        PreviewNavigationKind kind = PreviewNavigationPolicy.Classify(args.Uri, args.IsUserInitiated, _isOwnDocument);
        if (kind == PreviewNavigationKind.Document)
        {
            return;
        }
        args.Cancel = true;
        Follow(kind, args.Uri);
    }

    // Ein Verweis mit target="_blank" fragt nach einem neuen Fenster. Ohne Behandlung öffnete
    // WebView2 ein eigenes Fenster mit der fremden Seite.
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        Follow(PreviewNavigationPolicy.Classify(args.Uri, args.IsUserInitiated, _isOwnDocument), args.Uri);
    }

    private void Follow(PreviewNavigationKind kind, string uri)
    {
        if (kind == PreviewNavigationKind.External)
        {
            _linkLauncher.Open(new Uri(uri, UriKind.Absolute));
            return;
        }
        LogNavigationBlocked(_logger, kind);
    }
}
