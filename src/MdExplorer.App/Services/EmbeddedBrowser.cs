using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MdExplorer.App.Services;

/// <summary>
/// Start und Grundeinstellungen der drei eingebetteten Browserflächen: Vorschau, Hilfe, Graph.
/// </summary>
/// <remarks>
/// An einer Stelle, damit alle drei dieselben Startfehler abfangen und dieselben Funktionen
/// abschalten. Vorher fing das Hilfefenster nur einen der drei Fälle; fehlte die Laufzeit,
/// erschien bei jedem F1 der allgemeine Fehlerdialog.
/// </remarks>
[ExcludeFromCodeCoverage]
internal static partial class EmbeddedBrowser
{
    /// <summary>
    /// Startet die Fläche. Gelingt das nicht, bleibt sie leer, der Grund steht im Protokoll, und
    /// <paramref name="unavailableHint"/> wird sichtbar.
    /// </summary>
    /// <param name="view">Die Fläche.</param>
    /// <param name="unavailableHint">Hinweis, der an ihrer Stelle erklärt, warum nichts zu sehen ist.</param>
    /// <param name="logger">Protokoll des Fensters, zu dem die Fläche gehört.</param>
    /// <returns><see langword="true"/>, wenn die Fläche bereit ist.</returns>
    public static async Task<bool> TryStartAsync(WebView2 view, UIElement unavailableHint, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(unavailableHint);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            CoreWebView2Environment environment = await WebView2EnvironmentProvider.GetAsync().ConfigureAwait(true);
            await view.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
            return true;
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            LogRuntimeMissing(logger, exception);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FileNotFoundException)
        {
            LogStartFailed(logger, exception);
        }

        unavailableHint.Visibility = Visibility.Visible;
        return false;
    }

    /// <summary>Schaltet ab, was keine der Flächen braucht.</summary>
    /// <param name="core">Die gestartete Fläche.</param>
    /// <param name="allowScripts">Ob das Dokument eigene Skripte ausführt (Hilfe, Graph) oder nicht (Vorschau).</param>
    public static void Restrict(CoreWebView2 core, bool allowScripts)
    {
        ArgumentNullException.ThrowIfNull(core);

        CoreWebView2Settings settings = core.Settings;
        settings.IsScriptEnabled = allowScripts;
        // Die Anwendung spricht mit den Dokumenten nur über ExecuteScriptAsync; Nachrichten aus
        // dem Dokument nimmt niemand an.
        settings.IsWebMessageEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsStatusBarEnabled = false;
#if DEBUG
        settings.AreDevToolsEnabled = true;
#else
        settings.AreDevToolsEnabled = false;
#endif
    }

    [LoggerMessage(EventId = 420, Level = LogLevel.Error, Message = "WebView2-Runtime nicht installiert — die Anzeige bleibt leer.")]
    private static partial void LogRuntimeMissing(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 421, Level = LogLevel.Error, Message = "WebView2 ließ sich nicht starten.")]
    private static partial void LogStartFailed(ILogger logger, Exception exception);
}
