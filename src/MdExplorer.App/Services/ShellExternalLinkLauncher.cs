using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Services;

/// <summary>
/// Öffnet Verweise über die Windows-Shell, also im Programm, das der Nutzer für das Schema
/// eingestellt hat.
/// </summary>
/// <remarks>
/// Welche Verweise hier ankommen, entscheidet <see cref="PreviewNavigationPolicy"/>. Die
/// Schema-Prüfung steht trotzdem noch einmal hier: Die Shell startet zu jedem registrierten
/// Schema ein Programm, und dieser Dienst darf nie zum Startknopf für etwas anderes werden.
/// </remarks>
internal sealed partial class ShellExternalLinkLauncher : IExternalLinkLauncher
{
    private readonly ILogger<ShellExternalLinkLauncher> _logger;
    private readonly Action<string> _startWithShell;

    /// <summary>Erzeugt den Dienst, der Verweise über die Windows-Shell öffnet.</summary>
    public ShellExternalLinkLauncher(ILogger<ShellExternalLinkLauncher> logger)
        : this(logger, StartWithShell)
    {
    }

    /// <summary>Erzeugt den Dienst mit eigenem Start, damit die Prüfung ohne Prozessstart testbar ist.</summary>
    internal ShellExternalLinkLauncher(ILogger<ShellExternalLinkLauncher> logger, Action<string> startWithShell)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(startWithShell);
        _logger = logger;
        _startWithShell = startWithShell;
    }

    /// <inheritdoc />
    public void Open(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeMailto))
        {
            LogRejected(_logger, uri.IsAbsoluteUri ? uri.Scheme : "relativ");
            return;
        }

        try
        {
            _startWithShell(uri.AbsoluteUri);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            // Kein Standardprogramm für das Schema oder Start verweigert. Der Klick bleibt
            // folgenlos; die Anwendung läuft weiter.
            LogLaunchFailed(_logger, exception, uri.Scheme);
        }
    }

    // Der eigentliche Prozessstart. Ihn auszuführen hieße im Test, einen Browser zu öffnen.
    [ExcludeFromCodeCoverage]
    private static void StartWithShell(string address)
    {
        using Process? process = Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
    }

    [LoggerMessage(EventId = 415, Level = LogLevel.Warning, Message = "Verweis mit Schema {Scheme} wird nicht an die Shell übergeben.")]
    private static partial void LogRejected(ILogger logger, string scheme);

    [LoggerMessage(EventId = 416, Level = LogLevel.Warning, Message = "Standardprogramm für Schema {Scheme} ließ sich nicht starten.")]
    private static partial void LogLaunchFailed(ILogger logger, Exception exception, string scheme);
}
