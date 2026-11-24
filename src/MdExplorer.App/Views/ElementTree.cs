using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace MdExplorer.App.Views;

/// <summary>
/// Schritte nach oben im Elementbaum, auch durch Elemente, die keine <see cref="Visual"/> sind.
/// </summary>
internal static class ElementTree
{
    /// <summary>Liefert das übergeordnete Element oder <see langword="null"/> an der Wurzel.</summary>
    /// <remarks>
    /// Ein Verweis oder Lauf im Text (<c>Hyperlink</c>, <c>Run</c>) ist ein ContentElement.
    /// <see cref="VisualTreeHelper.GetParent"/> wirft dafür, statt <see langword="null"/> zu
    /// liefern; solche Elemente gehen deshalb über den logischen Baum.
    /// </remarks>
    /// <param name="element">Das Element, von dem aus gesucht wird.</param>
    public static DependencyObject? ParentOf(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element is Visual or Visual3D
            ? VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element)
            : LogicalTreeHelper.GetParent(element);
    }
}
