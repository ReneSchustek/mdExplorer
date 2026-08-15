using System.Diagnostics.CodeAnalysis;
using System.Windows.Controls;

namespace MdExplorer.App.Views.Panels;

/// <summary>
/// Mittleres Panel mit Suchfeld und Trefferliste.
/// </summary>
/// <remarks>
/// Das Leeren per <c>Escape</c> bringt der Baustein selbst mit — hier bleibt nur der
/// Einstieg für das Tastenkürzel des Hauptfensters.
/// </remarks>
[ExcludeFromCodeCoverage]
internal sealed partial class SearchPanel : UserControl
{
    /// <summary>Erstellt das Panel.</summary>
    public SearchPanel()
    {
        InitializeComponent();
    }

    /// <summary>Fokussiert das Suchfeld — wird vom MainWindow-Shortcut Strg+F gerufen.</summary>
    public void FocusQueryBox() => QuerySearchBox.FocusInput();
}
