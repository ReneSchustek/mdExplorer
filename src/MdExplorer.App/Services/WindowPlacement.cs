using System.Windows;

namespace MdExplorer.App.Services;

/// <summary>
/// Prüft eine gemerkte Fensterlage gegen den Bildschirm, bevor ein Fenster sie übernimmt.
/// </summary>
/// <remarks>
/// Nach dem Abstecken eines zweiten Bildschirms läge ein Fenster sonst außerhalb jeder
/// sichtbaren Fläche und ließe sich weder sehen noch greifen.
/// </remarks>
internal static class WindowPlacement
{
    // Von der Titelleiste muss so viel sichtbar sein, dass man das Fenster greifen und
    // verschieben kann.
    private const double GrabbableTitleWidth = 120;
    private const double TitleBarHeight = 30;

    /// <summary>
    /// Liefert die Lage, mit der das Fenster öffnen soll, oder <see langword="null"/>, wenn die
    /// gemerkte Lage nicht mehr erreichbar ist und das Fenster mittig öffnen soll.
    /// </summary>
    /// <param name="saved">Die gemerkte Lage.</param>
    /// <param name="screen">Die Fläche aller Bildschirme zusammen.</param>
    /// <param name="minimumWidth">Kleinste Breite des Fensters.</param>
    /// <param name="minimumHeight">Kleinste Höhe des Fensters.</param>
    public static WindowGeometry? FitOnScreen(WindowGeometry saved, Rect screen, double minimumWidth, double minimumHeight)
    {
        ArgumentNullException.ThrowIfNull(saved);

        // Nie größer als der Bildschirm, nie kleiner als das Fenster erlaubt.
        double width = Math.Max(minimumWidth, Math.Min(saved.Width, screen.Width));
        double height = Math.Max(minimumHeight, Math.Min(saved.Height, screen.Height));

        Rect titleBar = Rect.Intersect(new Rect(saved.Left, saved.Top, width, TitleBarHeight), screen);
        if (titleBar.IsEmpty || titleBar.Width < GrabbableTitleWidth || titleBar.Height < TitleBarHeight)
        {
            return null;
        }

        return new WindowGeometry(saved.Left, saved.Top, width, height);
    }
}
