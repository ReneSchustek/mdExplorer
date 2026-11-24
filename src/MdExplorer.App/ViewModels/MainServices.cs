using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.Services;
using MdExplorer.Indexer.Abstractions;
using MdExplorer.TagCloud.ViewModels;

namespace MdExplorer.App.ViewModels;

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
