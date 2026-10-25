using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.Services;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Messaging;
using MdExplorer.Core.Models;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Die Statuszeile zählt die indizierten Dateien je Wurzel und zeigt die Betriebsanzeige; beides
/// meldet sich aus dem Hintergrund und stellt sich am UI-Thread an.
/// </summary>
public sealed class StatusBarViewModelTests
{
    [Fact]
    public void IndexerProgress_SumsTheRootsAndOverwritesARepeatedRoot()
    {
        using MainTestHarness harness = new();
        StatusBarViewModel statusBar = harness.Main.StatusBar;

        harness.Indexer.RaiseProgress(@"F:\root-a", 17, isCompleted: false);
        harness.Indexer.RaiseProgress(@"F:\root-b", 5, isCompleted: false);
        harness.UiDispatcher.RunPending();
        Assert.Equal(22, statusBar.IndexedFileCount);

        harness.Indexer.RaiseProgress(@"F:\root-a", 30, isCompleted: true);
        harness.UiDispatcher.RunPending();
        Assert.Equal(35, statusBar.IndexedFileCount);
        Assert.Equal(MainTestHarness.FixedUtc, statusBar.LastIndexerProgressUtc);
    }

    /// <remarks>
    /// Die Meldung kommt vom Thread des Indexers. Hielte sie den UI-Thread an, bis die Anzeige
    /// gesetzt ist, wartete der Indexer bei jeder Datei auf die Oberfläche.
    /// </remarks>
    [Fact]
    public void IndexerProgress_QueuesBehindTheUiWorkInsteadOfBlockingIt()
    {
        using MainTestHarness harness = new();

        harness.Indexer.RaiseProgress(@"F:\root-a", 17, isCompleted: false);

        Assert.Equal(0, harness.Main.StatusBar.IndexedFileCount);
        Assert.Equal(0, harness.UiDispatcher.InvokeCount);

        harness.UiDispatcher.RunPending();

        Assert.Equal(17, harness.Main.StatusBar.IndexedFileCount);
    }

    [Fact]
    public void LastIndexerProgress_IsShownInTheLocalTimeZone()
    {
        // Kein UTC in der Statusleiste: Wer nachsieht, wann zuletzt indiziert wurde,
        // vergleicht das mit seiner eigenen Uhr.
        using MainTestHarness harness = new();
        StatusBarViewModel statusBar = harness.Main.StatusBar;
        List<string> changed = [];
        statusBar.PropertyChanged += (_, args) => changed.Add(args.PropertyName ?? string.Empty);
        Assert.Null(statusBar.LastIndexerProgress);

        harness.Indexer.RaiseProgress(@"F:\root-a", 3, isCompleted: true);
        harness.UiDispatcher.RunPending();

        Assert.Equal(TimeZoneInfo.ConvertTimeFromUtc(MainTestHarness.FixedUtc, MainTestHarness.TestZone), statusBar.LastIndexerProgress);
        Assert.Contains(nameof(StatusBarViewModel.LastIndexerProgress), changed, StringComparer.Ordinal);
    }

    /// <remarks>
    /// Eine entfernte Wurzel zählt nicht weiter mit; auch die Groß- und Kleinschreibung des
    /// Pfades in den Einstellungen ändert daran nichts.
    /// </remarks>
    [Fact]
    public void SettingsChanged_WithARemovedRoot_DropsItsCount()
    {
        using MainTestHarness harness = new();
        harness.Indexer.RaiseProgress(@"F:\root-a", 17, isCompleted: true);
        harness.Indexer.RaiseProgress(@"F:\root-b", 5, isCompleted: true);
        AppSettings onlyA = AppSettings.Default with
        {
            Indexing = AppSettings.Default.Indexing with { Roots = [@"f:\ROOT-A"] },
        };

        _ = harness.Messenger.Send(new SettingsChangedMessage(AppSettings.Default, onlyA));
        harness.UiDispatcher.RunPending();

        Assert.Equal(17, harness.Main.StatusBar.IndexedFileCount);
    }

    [Fact]
    public void HealthChanged_ReachesTheStatusBarOverTheQueue()
    {
        using MainTestHarness harness = new();

        harness.HealthProvider.SetState(OperationHealth.Warning, "Indexer hängt.");

        Assert.Equal(OperationHealth.Healthy, harness.Main.StatusBar.Health);
        harness.UiDispatcher.RunPending();
        Assert.Equal(OperationHealth.Warning, harness.Main.StatusBar.Health);
        Assert.Equal("Indexer hängt.", harness.Main.StatusBar.HealthDetail);
    }

    [Fact]
    public void Dispose_StopsListening()
    {
        using MainTestHarness harness = new();

        harness.Main.Dispose();
        harness.HealthProvider.SetState(OperationHealth.Error, "Kritisch");
        harness.Indexer.RaiseProgress(@"F:\root-a", 9, isCompleted: true);

        Assert.Equal(0, harness.UiDispatcher.PendingCount);
    }
}
