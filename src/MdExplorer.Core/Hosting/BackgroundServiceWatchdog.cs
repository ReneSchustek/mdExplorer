namespace MdExplorer.Core.Hosting;

/// <summary>
/// Letzte Schutzschicht für <c>BackgroundService.ExecuteAsync</c>-Methoden. Eine ungefangene
/// Exception in einem BackgroundService lässt den Host den Service stillschweigend beenden;
/// die App läuft danach mit blindem Indexer, Parser oder Such-Maintainer weiter. Jeder Service
/// fängt deshalb mit <c>catch (Exception ex) when (BackgroundServiceWatchdog.IsRecoverable(ex, stoppingToken))</c>
/// alle erwartbaren Ausnahmen ab und protokolliert sie. Kritische Prozessfehler (Speichermangel,
/// Stapelüberlauf) gehen weiter nach oben, damit das Protokoll des Systems sie erfasst.
/// </summary>
public static class BackgroundServiceWatchdog
{
    /// <summary>
    /// Liefert <see langword="true"/>, wenn die Exception als erholbar gilt und vom äußersten
    /// Fang eines Dienstes oder Handlers absorbiert werden darf.
    /// </summary>
    /// <param name="exception">Die zu klassifizierende Exception.</param>
    /// <param name="stoppingToken">Das Stopp-Token des Dienstes.</param>
    /// <returns>
    /// <see langword="false"/> für Prozessfehler und für einen Abbruch, der vom Herunterfahren
    /// kommt; <see langword="true"/> für alles andere, auch für einen fremden Abbruch.
    /// </returns>
    /// <remarks>
    /// Nicht erholbar sind Speichermangel, Stapelüberlauf und Speicherverletzung: Danach ist der
    /// Prozess nicht mehr verlässlich. Alles andere ist erholbar, bewusst großzügig, weil dies die
    /// letzte Schicht ist. Ein Handler ohne eigenes Stopp-Token übergibt
    /// <see cref="CancellationToken.None"/>; ein Abbruch kommt dort immer von außen.
    /// <para>
    /// Ein <see cref="OperationCanceledException"/> ist nicht immer das Herunterfahren: Ein
    /// HTTP-Timeout oder ein verknüpftes Token einer Bibliothek melden sich genauso. Ließe der
    /// Dienst einen solchen Abbruch durch, endete <c>ExecuteAsync</c>, und mit der
    /// Voreinstellung <c>StopHost</c> führe die ganze Anwendung herunter.
    /// </para>
    /// </remarks>
    public static bool IsRecoverable(Exception exception, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is OperationCanceledException
            ? !stoppingToken.IsCancellationRequested
            : !IsProcessFailure(exception);
    }

    // Nach diesen Fehlern ist der Prozess nicht mehr in einem verlässlichen Zustand.
    private static bool IsProcessFailure(Exception exception) =>
        exception is OutOfMemoryException or StackOverflowException or AccessViolationException;
}
