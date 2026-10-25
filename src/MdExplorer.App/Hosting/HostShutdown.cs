using System.ComponentModel;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Hosting;

/// <summary>
/// Stoppt die Hintergrunddienste, bevor das Hauptfenster schließt.
/// </summary>
/// <remarks>
/// In <c>Application.OnExit</c> ist es dafür zu spät. Dort wartet WPF auf keine Fortsetzung:
/// Nach dem ersten <c>await</c> endet der Prozess, und Stoppen, Freigabe des Containers und das
/// Leeren des Protokolls blieben halb. Das Hauptfenster hält deshalb sein Schließen an, bis die
/// Dienste gestoppt sind, und schließt dann ein zweites Mal.
/// </remarks>
internal sealed partial class HostShutdown(Func<Task> stopServices, ILogger<HostShutdown> logger)
{
    private bool _stopStarted;

    /// <summary>Ob die Dienste gestoppt sind und das Fenster schließen darf.</summary>
    public bool IsCompleted { get; private set; }

    /// <summary>Hält das Schließen an, solange die Dienste laufen.</summary>
    /// <param name="args">Argumente des Closing-Ereignisses.</param>
    /// <returns><see langword="true"/>, wenn der Aufrufer jetzt <see cref="StopAsync"/> starten soll.</returns>
    public bool HoldClosing(CancelEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        // Hat ein früherer Handler schon abgebrochen (Rückfrage zu ungespeicherten Änderungen),
        // bleibt das Fenster ohnehin offen, und die Dienste werden weiter gebraucht.
        if (args.Cancel || IsCompleted)
        {
            return false;
        }

        args.Cancel = true;
        if (_stopStarted)
        {
            return false;
        }
        _stopStarted = true;
        return true;
    }

    /// <summary>
    /// Stoppt die Dienste. Schlägt das fehl, ist das protokolliert, und das Fenster darf trotzdem
    /// schließen; ein Fenster, das sich nicht schließen lässt, hilft niemandem.
    /// </summary>
    public async Task StopAsync()
    {
        Task stopping = stopServices();

        // Wartet, ohne zu werfen: Was das Stoppen auch beendet, das Fenster soll danach schließen,
        // und der Fehler steht vollständig im Protokoll.
        _ = await Task.WhenAny(stopping).ConfigureAwait(true);

        if (stopping.IsFaulted)
        {
            LogStopFailed(logger, stopping.Exception);
        }
        else if (stopping.IsCanceled)
        {
            LogStopCancelled(logger);
        }
        IsCompleted = true;
    }

    [LoggerMessage(EventId = 110, Level = LogLevel.Error, Message = "Hintergrunddienste ließen sich nicht sauber stoppen.")]
    private static partial void LogStopFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 111, Level = LogLevel.Warning, Message = "Stoppen der Hintergrunddienste abgebrochen.")]
    private static partial void LogStopCancelled(ILogger logger);
}
