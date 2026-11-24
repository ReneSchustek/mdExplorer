using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using MdExplorer.Presentation;

namespace MdExplorer.App.Tests.Views;

/// <summary>
/// Eine Live-Region sagt ihre Änderung an. In WPF genügt dafür die Live-Einstellung allein nicht:
/// Erst das Ereignis <c>LiveRegionChanged</c> lässt einen Screenreader lesen.
/// </summary>
public sealed class LiveRegionTests
{
    [Fact]
    public void Announce_MarksTheElementAsAPoliteLiveRegion() => StaRunner.Run(() =>
    {
        TextBlock sut = new();

        LiveRegion.SetAnnounce(sut, true);

        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(sut));
    });

    /// <remarks>
    /// Die Ansage geht über den Automatisierungs-Peer des Elements; dass er nach der Aktualisierung
    /// besteht, zeigt, dass die Ansage ausgelöst wurde. Vorher gibt es keinen.
    /// </remarks>
    [Fact]
    public void Announce_ReactsToAnUpdateOfTheBinding() => StaRunner.Run(() =>
    {
        TextBlock sut = new();
        Status source = new() { Text = "erst" };
        _ = sut.SetBinding(TextBlock.TextProperty, new Binding(nameof(Status.Text)) { Source = source, NotifyOnTargetUpdated = true });
        LiveRegion.SetAnnounce(sut, true);
        Assert.Null(UIElementAutomationPeer.FromElement(sut));

        sut.GetBindingExpression(TextBlock.TextProperty)!.UpdateTarget();

        Assert.NotNull(UIElementAutomationPeer.FromElement(sut));
    });

    /// <remarks>
    /// Wer im Markup nur die Live-Einstellung setzt, glaubt an eine Ansage, die nie kommt.
    /// </remarks>
    [Fact]
    public void NoView_SetsTheLiveSettingWithoutTheAnnouncement()
    {
        string[] findings =
        [
            .. Directory.EnumerateFiles(RepositoryPaths.Source(), "*.xaml", SearchOption.AllDirectories)
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(file => File.ReadAllText(file).Contains("AutomationProperties.LiveSetting", StringComparison.Ordinal))
                .Select(Path.GetFileName)
                .OfType<string>(),
        ];

        Assert.True(findings.Length == 0, "LiveSetting ohne LiveRegion.Announce: " + string.Join(", ", findings));
    }

    /// <summary>Quelle einer Bindung, deren Wert der Test von Hand überträgt.</summary>
    private sealed class Status
    {
        public string Text { get; set; } = string.Empty;
    }
}
