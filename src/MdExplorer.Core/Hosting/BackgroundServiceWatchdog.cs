namespace MdExplorer.Core.Hosting;

/// <summary>
/// Letzte Schutzschicht für <c>BackgroundService.ExecuteAsync</c>-Methoden. Eine ungefangene
/// Exception in einem BackgroundService lässt den Host den Service stillschweigend beenden —
/// die App läuft danach mit blindem Indexer / Parser / Search-Maintainer. Der Watchdog erlaubt
/// pro Service ein `catch (Exception ex) when (BackgroundServiceWatchdog.IsRecoverable(ex))`,
/// das alle erwartbaren Library-Exceptions abfängt und den Service ordentlich loggt, kritische
/// Process-Errors (OOM, StackOverflow) aber weiter durchreicht, damit das System-Logging greifen
/// kann.
/// </summary>
public static class BackgroundServiceWatchdog
{
    /// <summary>
    /// Liefert <see langword="true"/>, wenn die übergebene Exception als „erholbar" gilt und
    /// von einem BackgroundService-Top-Level-Catch absorbiert werden darf.
    /// </summary>
    /// <param name="exception">Die zu klassifizierende Exception.</param>
    /// <remarks>
    /// Nicht erholbar (= weitergeben):
    /// <list type="bullet">
    ///   <item><see cref="OutOfMemoryException"/> — Prozess ist im inkonsistenten Zustand.</item>
    ///   <item><see cref="StackOverflowException"/> — wird ohnehin nicht abfangbar.</item>
    ///   <item><see cref="AccessViolationException"/> — Memory-Corruption.</item>
    ///   <item><see cref="OperationCanceledException"/> — Cancellation ist kein Fehler.</item>
    /// </list>
    /// Erholbar: alles andere. Bewusst großzügig, weil der Watchdog die letzte Schicht ist —
    /// jeder eingehende Catch-Pfad ist eine spezifischere Mauer davor.
    /// </remarks>
    public static bool IsRecoverable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is not OutOfMemoryException
            and not StackOverflowException
            and not AccessViolationException
            and not OperationCanceledException;
    }
}
