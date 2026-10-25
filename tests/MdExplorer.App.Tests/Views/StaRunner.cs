using System.Windows.Threading;

namespace MdExplorer.App.Tests.Views;

/// <summary>
/// Führt eine Zusicherung auf einem STA-Thread aus.
/// </summary>
/// <remarks>
/// <para>
/// Alles aus WPF verlangt einen Thread im Einzelfaden-Modus; der Testläufer stellt keinen.
/// Ohne diesen Umweg lässt sich kein einziges Bedienelement erzeugen, und die Bausteine der
/// Gestaltungslinie blieben ungeprüft.
/// </para>
/// <para>
/// Der Thread betreibt einen eigenen Dispatcher, und die Zusicherung läuft über
/// <see cref="Dispatcher.Invoke(Action)"/>. Das reicht jede Ausnahme an den Aufrufer weiter,
/// gleich welcher Art. Eine Ausnahme, die auf dem Fremdthread liegen bliebe, risse den
/// Testprozess ab, statt einen Test rot zu machen.
/// </para>
/// </remarks>
internal static class StaRunner
{
    /// <summary>Führt <paramref name="assertion"/> auf einem eigenen STA-Thread aus.</summary>
    /// <param name="assertion">Die Zusicherung.</param>
    public static void Run(Action assertion)
    {
        ArgumentNullException.ThrowIfNull(assertion);

        using ManualResetEventSlim ready = new();
        Dispatcher? dispatcher = null;

        Thread thread = new(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        ready.Wait();

        try
        {
            dispatcher!.Invoke(assertion); // vor ready.Set() gesetzt, ready.Wait() wartet darauf
        }
        finally
        {
            dispatcher!.InvokeShutdown(); // dasselbe Feld wie oben
            thread.Join();
        }
    }
}
