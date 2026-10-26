using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Hosting;

/// <summary>
/// Hält fest, dass diese Instanz die erste des Benutzers ist; solange sie lebt, startet keine
/// zweite auf derselben Datenbank.
/// </summary>
/// <remarks>
/// <para>
/// Entscheidend ist, ob der benannte Mutex neu angelegt wurde, nicht wer ihn besitzt. Das hängt
/// an keinem Thread: Ein Besitz gehörte dem Thread, der ihn erwarb, und WPF setzt nach einem
/// <c>await</c> nicht zwingend auf demselben fort. Endet die erste Instanz, auch durch Absturz,
/// schließt Windows ihren Griff, und der Name ist wieder frei.
/// </para>
/// <para>
/// <see cref="NamedWaitHandleOptions.CurrentUserOnly"/> lässt nur den eigenen Benutzer an den
/// Mutex. Hat ein anderer Benutzer oder eine Instanz mit erhöhten Rechten den Namen angelegt,
/// scheitert das Öffnen. Das Programm startet dann trotzdem, nur ohne den Schutz vor einer
/// zweiten Instanz (<see cref="IsExclusive"/>); ein fremder Name soll den Start nicht verhindern.
/// </para>
/// </remarks>
internal sealed partial class InstanceLock : IDisposable
{
    private readonly Mutex? _mutex;

    private InstanceLock(Mutex? mutex, bool isFirstInstance)
    {
        _mutex = mutex;
        IsFirstInstance = isFirstInstance;
    }

    /// <summary>Öffnet oder legt den benannten Mutex an.</summary>
    /// <param name="name">Name der Sperre, ohne Namensraum-Vorsilbe.</param>
    /// <returns>Der Mutex und ob er dabei neu angelegt wurde.</returns>
    internal delegate (Mutex Mutex, bool CreatedNew) MutexOpener(string name);

    /// <summary>Wahr, wenn keine andere Instanz des Benutzers läuft oder sich das nicht feststellen ließ.</summary>
    public bool IsFirstInstance { get; }

    /// <summary>Wahr, wenn die Sperre tatsächlich gehalten wird und eine zweite Instanz sie sieht.</summary>
    public bool IsExclusive => _mutex is not null;

    /// <summary>Legt die Sperre an oder stellt fest, dass es sie schon gibt.</summary>
    /// <param name="name">Name der Sperre, ohne Namensraum-Vorsilbe.</param>
    /// <param name="logger">Protokoll, falls sich die Sperre nicht anlegen lässt.</param>
    /// <returns>Die Sperre; ist diese Instanz nicht die erste, sofort wieder freigeben.</returns>
    public static InstanceLock Acquire(string name, ILogger logger) => Acquire(name, OpenMutex, logger);

    /// <summary>Gibt die Sperre frei.</summary>
    public void Dispose() => _mutex?.Dispose();

    /// <summary>Wie <see cref="Acquire(string, ILogger)"/>, mit austauschbarem Öffnen des Mutex.</summary>
    /// <param name="name">Name der Sperre, ohne Namensraum-Vorsilbe.</param>
    /// <param name="open">Öffnet oder legt den Mutex an.</param>
    /// <param name="logger">Protokoll, falls sich die Sperre nicht anlegen lässt.</param>
    /// <returns>Die Sperre.</returns>
    internal static InstanceLock Acquire(string name, MutexOpener open, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            (Mutex mutex, bool createdNew) = open(name);
            return new InstanceLock(mutex, createdNew);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
        {
            LogUnavailable(logger, name, exception);
            return new InstanceLock(mutex: null, isFirstInstance: true);
        }
    }

    [LoggerMessage(EventId = 1730, Level = LogLevel.Warning, Message = "Sperre {LockName} lässt sich nicht anlegen; MdExplorer läuft ohne Schutz vor einer zweiten Instanz.")]
    private static partial void LogUnavailable(ILogger logger, string lockName, Exception exception);

    private static (Mutex Mutex, bool CreatedNew) OpenMutex(string name)
    {
        NamedWaitHandleOptions options = new() { CurrentUserOnly = true, CurrentSessionOnly = false };
        Mutex mutex = new(initiallyOwned: false, name, options, out bool createdNew);
        return (mutex, createdNew);
    }
}
