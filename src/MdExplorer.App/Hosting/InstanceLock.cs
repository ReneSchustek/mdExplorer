using System.Threading;

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
/// Mutex. Hat ein anderer Benutzer den Namen vorher belegt, scheitert das Öffnen mit einer
/// Ausnahme, statt diese Instanz für eine zweite zu halten.
/// </para>
/// </remarks>
internal sealed class InstanceLock : IDisposable
{
    private readonly Mutex _mutex;

    private InstanceLock(Mutex mutex, bool isFirstInstance)
    {
        _mutex = mutex;
        IsFirstInstance = isFirstInstance;
    }

    /// <summary>Wahr, wenn keine andere Instanz des Benutzers läuft.</summary>
    public bool IsFirstInstance { get; }

    /// <summary>Legt die Sperre an oder stellt fest, dass es sie schon gibt.</summary>
    /// <param name="name">Name der Sperre, ohne Namensraum-Vorsilbe.</param>
    /// <returns>Die Sperre; ist diese Instanz nicht die erste, sofort wieder freigeben.</returns>
    public static InstanceLock Acquire(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        NamedWaitHandleOptions options = new() { CurrentUserOnly = true, CurrentSessionOnly = false };
        Mutex mutex = new(initiallyOwned: false, name, options, out bool createdNew);
        return new InstanceLock(mutex, createdNew);
    }

    /// <summary>Gibt die Sperre frei.</summary>
    public void Dispose() => _mutex.Dispose();
}
