using System.Diagnostics.CodeAnalysis;
using System.Windows.Controls;

namespace MdExplorer.App.Views.Panels;

/// <summary>
/// Tab-Panel mit der flachen Liste aller indizierten Markdown-Dateien. Datenfluss
/// läuft komplett über das gebundene <see cref="MdExplorer.App.ViewModels.AllFilesViewModel"/>
/// — Code-Behind beschränkt sich auf den partial-Class-Stub fürs XAML-Loading.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed partial class AllFilesPanel : UserControl
{
    /// <summary>Erstellt das Panel.</summary>
    public AllFilesPanel()
    {
        InitializeComponent();
    }
}
