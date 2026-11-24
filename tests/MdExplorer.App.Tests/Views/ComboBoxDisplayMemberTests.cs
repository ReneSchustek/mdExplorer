using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MdExplorer.App.Logging;

namespace MdExplorer.App.Tests.Views;

/// <summary>
/// Eine ComboBox mit dem Stil der Anwendung zeigt im geschlossenen Feld den Text aus
/// <c>DisplayMemberPath</c>, nicht den Datensatz selbst.
/// </summary>
/// <remarks>
/// WPF setzt <c>DisplayMemberPath</c> über den <c>ItemTemplateSelector</c> um. Reicht das eigene
/// Template ihn nicht an das Auswahlfeld weiter, steht dort <c>ToString()</c>, im Protokoll also
/// <c>LogLevelFilter { MinimumLevel = Trace, Display = Alle }</c>. Die aufgeklappte Liste sah
/// dabei richtig aus, deshalb fiel es erst spät auf.
/// </remarks>
public sealed class ComboBoxDisplayMemberTests
{
    [Fact]
    public void TheClosedComboBox_ShowsTheDisplayMemberOfTheSelectedItem() => StaRunner.Run(() =>
    {
        ApplicationResources.Use(() =>
        {
            ComboBox sut = new()
            {
                ItemsSource = LogEntryFilter.Levels,
                DisplayMemberPath = nameof(LogLevelFilter.Display),
                SelectedIndex = 0,
            };
            // Das Fenster ist die Wurzel für den Layoutdurchlauf; gezeigt wird es nicht. Ohne
            // sichtbares Fenster wendet WPF das Template der ComboBox erst auf Aufforderung an.
            Window host = new() { Content = sut, Width = 400, Height = 80 };
            host.Measure(new Size(400, 80));
            host.Arrange(new Rect(0, 0, 400, 80));
            host.UpdateLayout();
            _ = sut.ApplyTemplate();
            sut.Measure(new Size(400, 40));
            sut.Arrange(new Rect(0, 0, 400, 40));
            sut.UpdateLayout();

            List<string> texts = [.. TextsBelow(sut)];
            host.Close();

            Assert.Contains(LogEntryFilter.Levels[0].Display, texts);
            Assert.DoesNotContain(texts, text => text.Contains(nameof(LogLevelFilter), StringComparison.Ordinal));
        });
    });

    private static IEnumerable<string> TextsBelow(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is TextBlock text)
            {
                yield return text.Text;
            }

            foreach (string nested in TextsBelow(child))
            {
                yield return nested;
            }
        }
    }
}
