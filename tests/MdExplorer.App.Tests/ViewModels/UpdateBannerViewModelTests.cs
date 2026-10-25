using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.Messaging;
using MdExplorer.App.ViewModels;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>Der Hinweis auf eine neuere Fassung über dem Hauptfenster.</summary>
public sealed class UpdateBannerViewModelTests
{
    /// <remarks>
    /// Die Nachricht kommt aus dem Hintergrunddienst und damit von einem fremden Thread. Gesetzt
    /// wird erst, wenn der UI-Thread an der Reihe ist.
    /// </remarks>
    [Fact]
    public void UpdateMessage_ShowsTheHintOnceTheUiThreadRuns()
    {
        using MainTestHarness harness = new();
        UpdateBannerViewModel banner = harness.Main.UpdateBanner;
        Uri releaseUrl = new("https://example.invalid/release");

        _ = harness.Messenger.Send(new UpdateAvailableMessage("1.2.3", releaseUrl));

        Assert.False(banner.IsUpdateAvailable);
        harness.UiDispatcher.RunPending();
        Assert.True(banner.IsUpdateAvailable);
        Assert.Equal("1.2.3", banner.UpdateVersion, StringComparer.Ordinal);
        Assert.Equal(releaseUrl, banner.UpdateReleaseUrl);
    }

    [Fact]
    public void Receive_WithoutMessage_Throws()
    {
        using MainTestHarness harness = new();

        _ = Assert.Throws<ArgumentNullException>(() => harness.Main.UpdateBanner.Receive(null!));
    }

    [Fact]
    public void Dismiss_HidesTheHint()
    {
        using MainTestHarness harness = new();
        _ = harness.Messenger.Send(new UpdateAvailableMessage("1.2.3", new Uri("https://example.invalid/r")));
        harness.UiDispatcher.RunPending();

        harness.Main.UpdateBanner.DismissCommand.Execute(null);

        Assert.False(harness.Main.UpdateBanner.IsUpdateAvailable);
    }

    [Fact]
    public void Dispose_StopsListening()
    {
        using MainTestHarness harness = new();

        harness.Main.Dispose();
        _ = harness.Messenger.Send(new UpdateAvailableMessage("1.2.3", new Uri("https://example.invalid/r")));

        Assert.Equal(0, harness.UiDispatcher.PendingCount);
    }
}
