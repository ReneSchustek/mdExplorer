using MdExplorer.App.ViewModels;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Ein schmales Fenster blendet die Tag-Cloud aus, ohne die gespeicherte Wahl zu ändern. Schaltet
/// der Nutzer sie dort ausdrücklich ein, bleibt sie.
/// </summary>
public sealed class TagCloudVisibilityTests
{
    private const double Wide = 1280.0;
    private const double Narrow = 900.0;

    private readonly TagCloudVisibility _sut = new();

    [Fact]
    public void IsShown_InAWideWindow_FollowsThePreference()
    {
        _ = _sut.UpdateWidth(Wide);

        Assert.True(_sut.IsShown(preferred: true));
        Assert.False(_sut.IsShown(preferred: false));
    }

    [Fact]
    public void IsShown_InANarrowWindow_HidesEvenWhenPreferred()
    {
        Assert.True(_sut.UpdateWidth(Narrow));

        Assert.True(_sut.IsNarrow);
        Assert.False(_sut.IsShown(preferred: true));
    }

    [Fact]
    public void UpdateWidth_AtTheBoundary_CountsAsWide()
    {
        Assert.False(_sut.UpdateWidth(TagCloudVisibility.NarrowWidth));
        Assert.False(_sut.IsNarrow);
        Assert.True(_sut.UpdateWidth(TagCloudVisibility.NarrowWidth - 0.5));
    }

    [Fact]
    public void UpdateWidth_WithoutCrossingTheBoundary_ReportsNoChange()
    {
        _ = _sut.UpdateWidth(Narrow);

        Assert.False(_sut.UpdateWidth(Narrow - 100.0));
    }

    [Fact]
    public void Toggle_InANarrowWindow_ShowsItAndKeepsThePreference()
    {
        _ = _sut.UpdateWidth(Narrow);

        bool preferred = _sut.Toggle(preferred: true);

        Assert.True(preferred);
        Assert.True(_sut.IsShown(preferred));
    }

    [Fact]
    public void Toggle_InANarrowWindow_ThenHidingAgain_ClearsThePreference()
    {
        _ = _sut.UpdateWidth(Narrow);
        bool preferred = _sut.Toggle(preferred: true);

        preferred = _sut.Toggle(preferred);

        Assert.False(preferred);
        Assert.False(_sut.IsShown(preferred));
    }

    [Fact]
    public void ShownWhileNarrow_EndsWhenTheWindowCrossesTheBoundaryAgain()
    {
        _ = _sut.UpdateWidth(Narrow);
        bool preferred = _sut.Toggle(preferred: true);

        _ = _sut.UpdateWidth(Wide);
        Assert.True(_sut.IsShown(preferred));
        _ = _sut.UpdateWidth(Narrow);

        Assert.False(_sut.IsShown(preferred));
    }

    [Fact]
    public void Toggle_InAWideWindow_FlipsThePreference()
    {
        Assert.False(_sut.Toggle(preferred: true));
        Assert.True(_sut.Toggle(preferred: false));
    }
}
