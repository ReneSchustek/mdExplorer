using System.IO;
using System.Xml.Linq;

namespace MdExplorer.App.Tests;

/// <summary>
/// Hält den Aufbau des Hauptfensters fest: Kopfleiste statt Menüleiste, und jeder Symbolknopf
/// sagt, was er tut.
/// </summary>
/// <remarks>
/// Ein Symbol allein trägt für Screenreader nichts und für Sehende nur, wenn man es kennt. Ein
/// neuer Knopf ohne Namen und Tooltip fiele im Übersetzer nicht auf, sondern erst dem Nutzer.
/// </remarks>
public sealed class MainWindowLayoutGuardTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void TheMainWindow_HasNoMenuBar()
    {
        Assert.Empty(MainWindow().Descendants(Presentation + "Menu"));
    }

    [Fact]
    public void EveryIconButton_CarriesANameAndATooltip()
    {
        XElement[] iconButtons =
        [
            .. MainWindow().Descendants()
                .Where(element => ((string?)element.Attribute("Style"))?.Contains("Icon", StringComparison.Ordinal) == true),
        ];

        Assert.NotEmpty(iconButtons);
        Assert.All(iconButtons, button =>
        {
            Assert.StartsWith("{l:Loc ", (string?)button.Attribute("ToolTip") ?? string.Empty, StringComparison.Ordinal);
            Assert.StartsWith("{l:Loc ", (string?)button.Attribute("AutomationProperties.Name") ?? string.Empty, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void TheTagCloudToggle_BindsBothWays()
    {
        // Mit Befehl statt Bindung wechselte der Schalter über die Bedienungshilfen nur den Haken.
        XElement toggle = Assert.Single(MainWindow().Descendants(Presentation + "ToggleButton"));

        Assert.Equal("{Binding IsTagCloudShown, Mode=TwoWay}", (string?)toggle.Attribute("IsChecked"));
        Assert.Null(toggle.Attribute("Command"));
    }

    private static XElement MainWindow() =>
        XElement.Load(Path.Combine(RepositoryPaths.Project("MdExplorer.App"), "Views", "MainWindow.xaml"));
}
