using MdExplorer.App.Services.Help;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels.Help;
using MdExplorer.App.Views.Help;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.App.Tests.Views;

/// <summary>
/// Die Fenster nehmen Fläche, Schriftfarbe und Schrift aus dem Fenster-Style der Anwendung.
/// </summary>
/// <remarks>
/// Ein Style ohne Schlüssel greift nur beim genau passenden Typ. Jedes Fenster der Anwendung ist
/// aber ein eigener, abgeleiteter Typ; ohne ausdrücklichen Verweis bliebe es systemweiß, auch im
/// dunklen Erscheinungsbild.
/// </remarks>
public sealed class WindowStyleTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AWindowOfTheApp_TakesSurfaceAndTextColourFromTheAppStyle() => StaRunner.Run(() =>
    {
        ApplicationResources.Use(() =>
        {
            AboutWindow sut = new(new AboutViewModel(new AboutInfoProvider(new FakeTimeProvider(FixedNow)), new FakeTimeProvider(FixedNow)), new RecordingLinkLauncher());

            Assert.Same(ApplicationResources.Resource("AppBackgroundBrush"), sut.Background);
            Assert.Same(ApplicationResources.Resource("TextPrimaryBrush"), sut.Foreground);
            Assert.Same(ApplicationResources.Resource("AppFontFamily"), sut.FontFamily);
        });
    });
}
