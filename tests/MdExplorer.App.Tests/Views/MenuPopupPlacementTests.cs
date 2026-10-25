using System.Windows;
using System.Windows.Controls.Primitives;
using MdExplorer.App.Views.Menus;

namespace MdExplorer.App.Tests.Views;

/// <summary>
/// Hält fest, wo das Aufklappteil eines Menütitels landet. Meldet Windows „Menüs rechtsbündig
/// ausrichten", hängt WPF es von sich aus an die rechte Kante des Titels; bei „Datei" ganz
/// links liefe es dann über den Fensterrand hinaus.
/// </summary>
public sealed class MenuPopupPlacementTests
{
    private static readonly Size PopupSize = new(220, 140);
    private static readonly Size TitleSize = new(60, 28);

    [Fact]
    public void BelowLeftAligned_PutsThePopupUnderTheTitle_FlushWithItsLeftEdge()
    {
        CustomPopupPlacement[] suggestions = MenuPopupPlacement.BelowLeftAligned(PopupSize, TitleSize, default);

        CustomPopupPlacement first = suggestions[0];
        Assert.Equal(0d, first.Point.X);
        Assert.Equal(TitleSize.Height, first.Point.Y);
    }

    [Fact]
    public void BelowLeftAligned_OffersAnUpwardFallback_ForTitlesNearTheScreenEdge()
    {
        // Reicht der Platz nach unten nicht, greift WPF auf den zweiten Vorschlag zurück.
        // Ohne einen solchen bliebe nur die Voreinstellung und damit der Versatz nach links.
        CustomPopupPlacement[] suggestions = MenuPopupPlacement.BelowLeftAligned(PopupSize, TitleSize, default);

        Assert.Equal(2, suggestions.Length);
        CustomPopupPlacement second = suggestions[1];
        Assert.Equal(0d, second.Point.X);
        Assert.Equal(-PopupSize.Height, second.Point.Y);
    }

    [Fact]
    public void BelowLeftAligned_NeverShiftsThePopupToTheLeftOfItsTitle()
    {
        // Eine negative X-Verschiebung schiebt das Aufklappteil des ersten Menütitels aus
        // dem Fenster heraus.
        CustomPopupPlacement[] suggestions = MenuPopupPlacement.BelowLeftAligned(PopupSize, TitleSize, default);

        Assert.All(suggestions, suggestion => Assert.True(suggestion.Point.X >= 0d));
    }
}
