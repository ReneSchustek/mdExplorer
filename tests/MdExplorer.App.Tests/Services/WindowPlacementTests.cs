using System.Windows;
using MdExplorer.App.Services;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Eine gemerkte Fensterlage gilt nur, solange man das Fenster dort auch greifen kann. Nach dem
/// Abstecken eines Bildschirms öffnete das Hilfefenster sonst außerhalb jeder sichtbaren Fläche.
/// </summary>
public sealed class WindowPlacementTests
{
    private const double MinimumWidth = 640;
    private const double MinimumHeight = 420;

    // Ein Bildschirm mit 1920 × 1080 am Ursprung.
    private static readonly Rect SingleScreen = new(0, 0, 1920, 1080);

    [Fact]
    public void FitOnScreen_PlaceOnTheScreen_IsKept()
    {
        WindowGeometry saved = new(100, 80, 980, 720);

        Assert.Equal(saved, WindowPlacement.FitOnScreen(saved, SingleScreen, MinimumWidth, MinimumHeight));
    }

    [Fact]
    public void FitOnScreen_PlaceOnARemovedSecondScreen_OpensCentred()
    {
        // Gemerkt auf einem zweiten Bildschirm rechts daneben, der jetzt fehlt.
        WindowGeometry saved = new(2200, 100, 980, 720);

        Assert.Null(WindowPlacement.FitOnScreen(saved, SingleScreen, MinimumWidth, MinimumHeight));
    }

    [Fact]
    public void FitOnScreen_TitleBarAboveTheScreen_OpensCentred()
    {
        WindowGeometry saved = new(100, -400, 980, 720);

        Assert.Null(WindowPlacement.FitOnScreen(saved, SingleScreen, MinimumWidth, MinimumHeight));
    }

    [Fact]
    public void FitOnScreen_OnlyAStripAtTheRightEdgeVisible_OpensCentred()
    {
        WindowGeometry saved = new(1860, 100, 980, 720);

        Assert.Null(WindowPlacement.FitOnScreen(saved, SingleScreen, MinimumWidth, MinimumHeight));
    }

    [Fact]
    public void FitOnScreen_LargerThanTheScreen_ShrinksToIt()
    {
        WindowGeometry saved = new(0, 0, 2560, 1440);

        WindowGeometry? fitted = WindowPlacement.FitOnScreen(saved, SingleScreen, MinimumWidth, MinimumHeight);

        Assert.Equal(new WindowGeometry(0, 0, 1920, 1080), fitted);
    }

    [Fact]
    public void FitOnScreen_SmallerThanAllowed_GrowsToTheMinimum()
    {
        WindowGeometry saved = new(100, 100, 200, 100);

        WindowGeometry? fitted = WindowPlacement.FitOnScreen(saved, SingleScreen, MinimumWidth, MinimumHeight);

        Assert.Equal(new WindowGeometry(100, 100, MinimumWidth, MinimumHeight), fitted);
    }

    [Fact]
    public void FitOnScreen_ScreenLeftOfTheMainScreen_IsPartOfTheArea()
    {
        // Zwei Bildschirme, der zweite links: Die Fläche beginnt im Negativen.
        Rect twoScreens = new(-1920, 0, 3840, 1080);
        WindowGeometry saved = new(-1500, 50, 980, 720);

        Assert.Equal(saved, WindowPlacement.FitOnScreen(saved, twoScreens, MinimumWidth, MinimumHeight));
    }
}
