using System.Windows;
using System.Windows.Controls.Primitives;
using MdExplorer.App.Views.Menus;

namespace MdExplorer.App.Tests.Views;

/// <summary>
/// Hält fest, wo das Aufklappteil eines Menütitels landet. Auf Rechnern, an denen Windows
/// „Menüs rechtsbündig ausrichten" meldet, hängte WPF es an die rechte Kante des Titels —
/// bei „Datei" ganz links lief es dadurch über den Fensterrand hinaus.
/// </summary>
public sealed class MenuPopupPlacementTests
{
    private static readonly Size PopupSize = new(220, 140);
    private static readonly Size TitleSize = new(60, 28);

    [Fact]
    public void BelowLeftAligned_PutsThePopupUnderTheTitle_FlushWithItsLeftEdge()
    {
        CustomPopupPlacement[] vorschlaege = MenuPopupPlacement.BelowLeftAligned(PopupSize, TitleSize, default);

        CustomPopupPlacement erster = vorschlaege[0];
        Assert.Equal(0d, erster.Point.X);
        Assert.Equal(TitleSize.Height, erster.Point.Y);
    }

    [Fact]
    public void BelowLeftAligned_OffersAnUpwardFallback_ForTitlesNearTheScreenEdge()
    {
        // Reicht der Platz nach unten nicht, greift WPF auf den zweiten Vorschlag zurück.
        // Ohne einen solchen bliebe nur die Voreinstellung — und damit der alte Versatz.
        CustomPopupPlacement[] vorschlaege = MenuPopupPlacement.BelowLeftAligned(PopupSize, TitleSize, default);

        Assert.Equal(2, vorschlaege.Length);
        CustomPopupPlacement zweiter = vorschlaege[1];
        Assert.Equal(0d, zweiter.Point.X);
        Assert.Equal(-PopupSize.Height, zweiter.Point.Y);
    }

    [Fact]
    public void BelowLeftAligned_NeverShiftsThePopupToTheLeftOfItsTitle()
    {
        // Der eigentliche Fehler: eine negative X-Verschiebung schob das Aufklappteil
        // aus dem Fenster heraus.
        CustomPopupPlacement[] vorschlaege = MenuPopupPlacement.BelowLeftAligned(PopupSize, TitleSize, default);

        Assert.All(vorschlaege, vorschlag => Assert.True(vorschlag.Point.X >= 0d));
    }
}
