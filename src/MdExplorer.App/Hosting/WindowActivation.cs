using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows;

namespace MdExplorer.App.Hosting;

/// <summary>
/// Holt das Hauptfenster der laufenden Instanz nach vorn, wenn eine zweite ihr eine Datei reicht.
/// </summary>
/// <remarks>
/// Windows lässt nur den Prozess ein Fenster in den Vordergrund holen, der gerade die Eingabe des
/// Nutzers hat. Das ist die zweite Instanz, die Explorer nach dem Doppelklick startet, nicht die
/// laufende. Deshalb gibt die zweite ihr Recht vor der Übergabe ab; sonst blinkte die laufende nur
/// in der Taskleiste.
/// </remarks>
[ExcludeFromCodeCoverage]
internal static class WindowActivation
{
    /// <summary><c>ASFW_ANY</c> aus <c>winuser.h</c>: jeder Prozess darf das Recht übernehmen.</summary>
    private const int AnyProcess = -1;

    /// <summary>Gibt das Recht, ein Fenster nach vorn zu holen, an die laufende Instanz ab.</summary>
    /// <remarks>
    /// Die Kennung der laufenden Instanz ist hier nicht bekannt; das Recht gilt ohnehin nur bis
    /// zur nächsten Eingabe des Nutzers. Scheitert der Aufruf, blinkt das Fenster nur, statt nach
    /// vorn zu kommen; die Datei öffnet trotzdem.
    /// </remarks>
    public static void AllowRunningInstanceToComeForward() => _ = AllowSetForegroundWindow(AnyProcess);

    /// <summary>Stellt ein minimiertes Fenster wieder her und holt es nach vorn.</summary>
    /// <param name="window">Das Fenster.</param>
    public static void BringToFront(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.WindowState == WindowState.Minimized)
        {
            // Wiederherstellen statt auf Normal setzen: Ein zuvor maximiertes Fenster bleibt so
            // maximiert.
            SystemCommands.RestoreWindow(window);
        }
        _ = window.Activate();
    }

    // Nur aus dem Systemverzeichnis laden: Ohne diese Angabe sucht Windows die Bibliothek
    // zuerst neben der Anwendung; dort könnte jemand eine eigene hinterlegen.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
