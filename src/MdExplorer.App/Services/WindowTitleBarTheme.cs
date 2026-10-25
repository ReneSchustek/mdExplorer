using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MdExplorer.App.Services;

/// <summary>
/// Zieht die Titelleiste eines Fensters an das gewählte Erscheinungsbild.
/// </summary>
/// <remarks>
/// Die Titelleiste zeichnet Windows, nicht die Anwendung, und es färbt sie nur dann dunkel,
/// wenn das System dunkel steht. Wer in den Einstellungen „Dunkel" wählt, während Windows
/// hell läuft, bekäme sonst über jedem Fenster einen weißen Balken. Eine einzige Angabe an den
/// Fenstermanager genügt, um das zu ändern.
/// </remarks>
internal static class WindowTitleBarTheme
{
    /// <summary><c>DWMWA_USE_IMMERSIVE_DARK_MODE</c> aus <c>dwmapi.h</c>, gültig ab Windows 10 Version 2004.</summary>
    private const int UseImmersiveDarkMode = 20;
    // Dieselbe Angabe unter der undokumentierten Nummer, die Windows 10 vor Version 2004 erwartet.
    private const int UseImmersiveDarkModeBefore20H1 = 19;

    /// <summary>
    /// Setzt die Titelleiste des Fensters auf hell oder dunkel.
    /// </summary>
    /// <remarks>
    /// Scheitert der Aufruf (etwa auf einer älteren Windows-Fassung), bleibt die Leiste,
    /// wie sie ist. Das ist ein Schönheitsfehler und kein Grund, ein Fenster nicht zu zeigen.
    /// </remarks>
    /// <param name="window">Das betroffene Fenster.</param>
    /// <param name="isDarkMode">Ob die dunkle Belegung gilt.</param>
    public static void Apply(Window window, bool isDarkMode)
    {
        ArgumentNullException.ThrowIfNull(window);

        nint handle = new WindowInteropHelper(window).Handle;
        if (handle == 0)
        {
            // Vor dem Erzeugen des Fensters gibt es noch nichts zu färben.
            return;
        }

        int enabled = isDarkMode ? 1 : 0;
        if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(handle, UseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
        }
    }

    // Nur aus dem Systemverzeichnis laden: Ohne diese Angabe sucht Windows die Bibliothek
    // zuerst neben der Anwendung; dort könnte jemand eine eigene hinterlegen.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
