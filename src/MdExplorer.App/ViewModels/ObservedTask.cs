using MdExplorer.Core.Hosting;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Lässt eine Aufgabe laufen, ohne auf sie zu warten, und protokolliert ihren Fehler.
/// </summary>
/// <remarks>
/// Ein Klick in einer Liste startet einen Ladevorgang, auf den niemand wartet. Ein verworfener
/// Task schluckt seinen Fehler; er taucht erst als „unbeobachtete Ausnahme“ beim Aufräumen des
/// Speichers auf, ohne Bezug zum Klick. Hier wird er dort protokolliert, wo er entsteht.
/// Nicht erholbare Fehler (etwa fehlender Speicher) bleiben unberührt.
/// </remarks>
internal static partial class ObservedTask
{
    /// <summary>Beobachtet <paramref name="task"/> im Hintergrund.</summary>
    /// <param name="task">Die laufende Aufgabe.</param>
    /// <param name="logger">Wohin ein Fehler gemeldet wird.</param>
    public static void Forget(Task task, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(logger);

        _ = ObserveAsync(task, logger);
    }

    private static async Task ObserveAsync(Task task, ILogger logger)
    {
        try
        {
            await task.ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Abgebrochen von einem neueren Klick oder vom Beenden der Anwendung; kein Fehler.
        }
        catch (Exception exception) when (BackgroundServiceWatchdog.IsRecoverable(exception, CancellationToken.None))
        {
            LogBackgroundWorkFailed(logger, exception);
        }
    }

    [LoggerMessage(EventId = 330, Level = LogLevel.Error, Message = "Ein Vorgang im Hintergrund der Oberfläche ist gescheitert.")]
    private static partial void LogBackgroundWorkFailed(ILogger logger, Exception exception);
}
