using System.Windows;
using System.Windows.Controls.Primitives;

namespace MdExplorer.App.Views.Menus;

/// <summary>
/// Bestimmt, wo das Aufklappteil eines Menütitels erscheint.
/// </summary>
/// <remarks>
/// Windows kennt die Einstellung „Menüs rechtsbündig ausrichten" (gesetzt unter anderem von
/// der Stift- und Tablet-Einrichtung). Steht sie an, hängt WPF jedes Aufklappteil an die
/// rechte Kante seines Titels — bei „Datei" ganz links in der Leiste läuft es dadurch über
/// den Fensterrand hinaus, und die Einträge sind angeschnitten. Mit eigener Platzierung
/// entscheidet allein diese Klasse, und die Anwendung sieht überall gleich aus.
/// </remarks>
internal static class MenuPopupPlacement
{
    /// <summary>
    /// Linke Kante bündig zum Titel, direkt darunter. Reicht der Platz nach unten nicht,
    /// greift WPF auf den zweiten Vorschlag zurück und klappt nach oben auf.
    /// </summary>
    public static CustomPopupPlacementCallback BelowLeftAligned { get; } =
        (popupSize, targetSize, _) =>
        [
            new CustomPopupPlacement(new Point(0, targetSize.Height), PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new Point(0, -popupSize.Height), PopupPrimaryAxis.Horizontal),
        ];
}
