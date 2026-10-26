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

/// <summary>Die Dienste, auf die das <see cref="MainViewModel"/> zugreift.</summary>
/// <param name="DocumentLocator">Löst WikiLinks und Pfade auf.</param>
/// <param name="SettingsStore">Hält Spaltenbreiten und Fensterzustand.</param>
/// <param name="HealthProvider">Liefert die Betriebsanzeige der Statuszeile.</param>
/// <param name="UiDispatcher">Hebt Meldungen aus Hintergrund-Threads auf den UI-Thread.</param>
/// <param name="Indexer">Meldet den Fortschritt der Indexierung.</param>
/// <param name="Messenger">Liefert Update- und Einstellungsnachrichten.</param>
/// <param name="TimeProvider">Uhr für den Zeitpunkt des letzten Indexer-Laufs.</param>
internal sealed record MainServices(
    IDocumentLocator DocumentLocator,
    UiSettingsStore SettingsStore,
    IOperationHealthProvider HealthProvider,
    IUiDispatcher UiDispatcher,
    IIndexer Indexer,
    IMessenger Messenger,
    TimeProvider TimeProvider);
