using System.Windows.Controls;
using System.Windows.Documents;
using MdExplorer.App.Views;

namespace MdExplorer.App.Tests.Views;

/// <summary>
/// Der Weg nach oben im Elementbaum geht auch durch Elemente, die keine Visual sind.
/// </summary>
/// <remarks>
/// Ein <see cref="Hyperlink"/> im Text ist ein ContentElement. <c>VisualTreeHelper.GetParent</c>
/// wirft dafür, statt null zu liefern; wer per Tab auf einen Verweis kam, bekam den Fehlerdialog.
/// </remarks>
public sealed class ElementTreeTests
{
    [Fact]
    public void ParentOf_AHyperlinkInText_IsItsTextBlock() => StaRunner.Run(() =>
    {
        Hyperlink link = new(new Run("Release öffnen"));
        TextBlock text = new(link);

        Assert.Same(text, ElementTree.ParentOf(link));
    });

    [Fact]
    public void ParentOf_AVisual_IsItsVisualParent() => StaRunner.Run(() =>
    {
        Button button = new();
        StackPanel panel = new();
        _ = panel.Children.Add(button);

        Assert.Same(panel, ElementTree.ParentOf(button));
    });
}
