using System.ComponentModel;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Was die Tag-Cloud zeigt und was gespeichert wird, sind zweierlei: Ein schmales Fenster blendet
/// sie aus, die gespeicherte Wahl bleibt; ausblenden per Knopf ändert die Wahl.
/// </summary>
public sealed class MainViewModelTagCloudTests
{
    [Fact]
    public void UpdateWindowWidth_Narrow_HidesTheTagCloudWithoutChangingThePreference()
    {
        using MainTestHarness harness = new();
        harness.Main.IsTagCloudVisible = true;
        List<string?> changed = Record(harness);

        harness.Main.UpdateWindowWidth(900.0);

        Assert.False(harness.Main.IsTagCloudShown);
        Assert.True(harness.Main.IsTagCloudVisible);
        Assert.True(harness.ReadPersistedLayout().IsTagCloudVisible);
        Assert.Contains(nameof(harness.Main.IsTagCloudShown), changed);
    }

    [Fact]
    public void UpdateWindowWidth_BackToWide_ShowsTheTagCloudAgain()
    {
        using MainTestHarness harness = new();
        harness.Main.IsTagCloudVisible = true;
        harness.Main.UpdateWindowWidth(900.0);

        harness.Main.UpdateWindowWidth(1280.0);

        Assert.True(harness.Main.IsTagCloudShown);
    }

    [Fact]
    public void UpdateWindowWidth_WithoutCrossingTheBoundary_RaisesNothing()
    {
        using MainTestHarness harness = new();
        harness.Main.UpdateWindowWidth(1280.0);
        List<string?> changed = Record(harness);

        harness.Main.UpdateWindowWidth(1300.0);

        Assert.Empty(changed);
    }

    [Fact]
    public void IsTagCloudShown_SetFalseInAWideWindow_StoresThePreference()
    {
        using MainTestHarness harness = new();
        harness.Main.IsTagCloudVisible = true;

        harness.Main.IsTagCloudShown = false;

        Assert.False(harness.Main.IsTagCloudVisible);
        Assert.False(harness.ReadPersistedLayout().IsTagCloudVisible);
    }

    [Fact]
    public void IsTagCloudShown_SetTrueInANarrowWindow_ShowsItAndKeepsThePreference()
    {
        using MainTestHarness harness = new();
        harness.Main.IsTagCloudVisible = true;
        harness.Main.UpdateWindowWidth(900.0);
        List<string?> changed = Record(harness);

        harness.Main.IsTagCloudShown = true;

        Assert.True(harness.Main.IsTagCloudShown);
        Assert.True(harness.Main.IsTagCloudVisible);
        Assert.Contains(nameof(harness.Main.IsTagCloudShown), changed);
    }

    [Fact]
    public void IsTagCloudShown_SetToItsValue_ChangesNothing()
    {
        using MainTestHarness harness = new();
        harness.Main.IsTagCloudVisible = true;
        List<string?> changed = Record(harness);

        harness.Main.IsTagCloudShown = true;

        Assert.Empty(changed);
    }

    [Fact]
    public void HideTagCloud_HidesItAndStoresThePreference()
    {
        using MainTestHarness harness = new();
        harness.Main.IsTagCloudVisible = true;

        harness.Main.HideTagCloudCommand.Execute(null);

        Assert.False(harness.Main.IsTagCloudShown);
        Assert.False(harness.ReadPersistedLayout().IsTagCloudVisible);
    }

    private static List<string?> Record(MainTestHarness harness)
    {
        List<string?> changed = [];
        ((INotifyPropertyChanged)harness.Main).PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        return changed;
    }
}
