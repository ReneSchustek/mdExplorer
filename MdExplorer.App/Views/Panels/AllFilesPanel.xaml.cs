using System.Windows.Controls;

namespace MdExplorer.App.Views.Panels;

/// <summary>
/// Tab-Panel mit der flachen Liste aller indizierten Markdown-Dateien. Datenfluss
/// laeuft komplett ueber das gebundene <see cref="MdExplorer.App.ViewModels.AllFilesViewModel"/>
/// — Code-Behind beschraenkt sich auf den partial-Class-Stub fuers XAML-Loading.
/// </summary>
internal sealed partial class AllFilesPanel : UserControl
{
    /// <summary>Erstellt das Panel.</summary>
    public AllFilesPanel()
    {
        InitializeComponent();
    }
}
