using System.Windows;
using System.Windows.Controls;

namespace MdExplorer.App.Tests.Views;

/// <summary>
/// Die Register-Vorlage der Anwendung macht den Inhalt des Registers für Screenreader sichtbar.
/// </summary>
/// <remarks>
/// Der Automatisierungspartner eines Registers sucht den Inhalt unter einem festen Namen in der
/// Vorlage. Fehlt der Name, steht unter „Ordner“, „Alle Dateien“ und „Suche“ im
/// Automatisierungsbaum nichts, obwohl man auf dem Bildschirm alles sieht.
/// </remarks>
public sealed class TabControlTemplateTests
{
    [Fact]
    public void TheTemplate_NamesTheContentHostTheAutomationLooksFor() => StaRunner.Run(() =>
    {
        ApplicationResources.Use(() =>
        {
            TabControl sut = new() { Style = (Style)ApplicationResources.Resource(typeof(TabControl)) };
            _ = sut.Items.Add(new TabItem { Header = "Ordner", Content = new TextBlock() });

            Assert.True(sut.ApplyTemplate());

            _ = Assert.IsType<ContentPresenter>(sut.Template.FindName("PART_SelectedContentHost", sut));
        });
    });
}
