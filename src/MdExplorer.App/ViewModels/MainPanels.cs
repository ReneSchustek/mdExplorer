using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.Services;
using MdExplorer.Indexer.Abstractions;
using MdExplorer.TagCloud.ViewModels;

namespace MdExplorer.App.ViewModels;

/// <summary>Die Bereiche des Hauptfensters, die das <see cref="MainViewModel"/> verbindet.</summary>
/// <param name="FolderTree">Linke Spalte, Register Ordner.</param>
/// <param name="AllFiles">Linke Spalte, Register Alle Dateien.</param>
/// <param name="Search">Linke Spalte, Register Suche.</param>
/// <param name="DocumentPanel">Mittlere Spalte mit Vorschau, Editor und Bezügen.</param>
/// <param name="TagCloud">Zuschaltbare Tag-Cloud.</param>
/// <param name="RecentFiles">Verlauf der zuletzt geöffneten Dateien.</param>
internal sealed record MainPanels(
    FolderTreeViewModel FolderTree,
    AllFilesViewModel AllFiles,
    SearchViewModel Search,
    DocumentPanelViewModel DocumentPanel,
    TagCloudViewModel TagCloud,
    RecentFilesViewModel RecentFiles);
