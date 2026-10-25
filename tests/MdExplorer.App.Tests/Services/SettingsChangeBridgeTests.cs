using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.Core.Messaging;
using MdExplorer.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Jede gespeicherte Einstellung geht als Nachricht an die Oberfläche. Einen neuen Durchlauf des
/// Indexers löst nur eine Änderung aus, die den Bestand betrifft; ein späterer bricht den noch
/// laufenden ab. Ein erwartbarer Fehler im Durchlauf wird aufgefangen.
/// </summary>
/// <remarks>
/// Der Indexer der Tests schließt jeden Durchlauf synchron ab, solange er ihn nicht ausdrücklich
/// festhält. Nach dem Speichern ist der Durchlauf deshalb schon gelaufen; ein Abwarten braucht
/// es nicht.
/// </remarks>
public sealed class SettingsChangeBridgeTests
{
    private static readonly AppSettings WithRoot = AppSettings.Default with
    {
        Indexing = IndexingSettings.Default with { Roots = [@"C:\Notizen"] },
    };

    private static readonly AppSettings WithOtherRoot = AppSettings.Default with
    {
        Indexing = IndexingSettings.Default with { Roots = [@"C:\Andere"] },
    };

    [Fact]
    public async Task OnSettingsChanged_AfterStart_SendsTheMessage()
    {
        (SettingsChangeBridge sut, FakeSettingsService settings, _, StrongReferenceMessenger messenger) = Create();
        await sut.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        SettingsChangedMessage? received = null;
        messenger.Register<SettingsChangedMessage>(this, (_, message) => received = message);

        await settings.SaveAsync(WithRoot, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.NotNull(received);
        Assert.Same(WithRoot, received.Current);
        await sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    [Fact]
    public async Task OnSettingsChanged_WithNewRoots_RescansTheIndex()
    {
        (SettingsChangeBridge sut, FakeSettingsService settings, FakeIndexer indexer, _) = Create();
        await sut.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        await settings.SaveAsync(WithRoot, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(1, indexer.CallCount);
        await sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <remarks>
    /// Ein Wechsel von Erscheinungsbild, Schriftgröße oder Wartezeit ändert am Bestand nichts.
    /// Ein Voll-Scan dafür liest jede Datei erneut und hält bei Tausenden von Notizen minutenlang
    /// die Platte beschäftigt.
    /// </remarks>
    [Fact]
    public async Task OnSettingsChanged_OutsideTheIndexing_DoesNotRescan()
    {
        (SettingsChangeBridge sut, FakeSettingsService settings, FakeIndexer indexer, _) = Create();
        await sut.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        AppSettings otherLook = AppSettings.Default with
        {
            Appearance = AppearanceSettings.Default with { Theme = AppTheme.Dark, PreviewFontSize = 20 },
            Behavior = BehaviorSettings.Default with { SearchDebounceMs = 500 },
        };

        await settings.SaveAsync(otherLook, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(0, indexer.CallCount);
        await sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <remarks>
    /// Die Listen sind neue Objekte mit demselben Inhalt, wie nach jedem Laden aus der Datei. Ein
    /// Vergleich der Verweise hielte das für eine Änderung.
    /// </remarks>
    [Fact]
    public async Task OnSettingsChanged_WithEqualListsInNewObjects_DoesNotRescan()
    {
        (SettingsChangeBridge sut, FakeSettingsService settings, FakeIndexer indexer, _) = Create(WithRoot);
        await sut.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        AppSettings sameContent = WithRoot with
        {
            Indexing = WithRoot.Indexing with
            {
                Roots = [.. WithRoot.Indexing.Roots],
                ExclusionPatterns = [.. WithRoot.Indexing.ExclusionPatterns],
            },
        };

        await settings.SaveAsync(sameContent, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(0, indexer.CallCount);
        await sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("ExclusionPatterns")]
    [InlineData("UiExcludedFolders")]
    [InlineData("AutoExtractHashtags")]
    public async Task OnSettingsChanged_InAnyIndexingField_Rescans(string field)
    {
        (SettingsChangeBridge sut, FakeSettingsService settings, FakeIndexer indexer, _) = Create();
        await sut.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        IndexingSettings indexing = IndexingSettings.Default;
        IndexingSettings changed = field switch
        {
            "ExclusionPatterns" => indexing with { ExclusionPatterns = ["**/archiv/**"] },
            "UiExcludedFolders" => indexing with { UiExcludedFolders = [@"C:\Notizen\alt"] },
            _ => indexing with { AutoExtractHashtags = !indexing.AutoExtractHashtags },
        };

        await settings.SaveAsync(AppSettings.Default with { Indexing = changed }, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(1, indexer.CallCount);
        await sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <remarks>
    /// Zwei Speichervorgänge kurz hintereinander: Der erste Durchlauf arbeitet mit einem Stand, der
    /// schon nicht mehr gilt. Er wird abgebrochen, statt den Bestand ein zweites Mal ganz zu lesen.
    /// </remarks>
    [Fact]
    public async Task OnSettingsChanged_WhileARescanRuns_CancelsTheEarlierOne()
    {
        (SettingsChangeBridge sut, FakeSettingsService settings, FakeIndexer indexer, _) = Create();
        indexer.HoldScans = true;
        await sut.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        await settings.SaveAsync(WithRoot, TestContext.Current.CancellationToken).ConfigureAwait(true);
        await settings.SaveAsync(WithOtherRoot, TestContext.Current.CancellationToken).ConfigureAwait(true);

        IReadOnlyList<CancellationToken> scans = indexer.ScanTokens;
        Assert.Equal(2, scans.Count);
        Assert.True(scans[0].IsCancellationRequested);
        Assert.False(scans[1].IsCancellationRequested);
        await sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    [Fact]
    public async Task StopAsync_WhileARescanRuns_CancelsItAndUnsubscribes()
    {
        (SettingsChangeBridge sut, FakeSettingsService settings, FakeIndexer indexer, StrongReferenceMessenger messenger) = Create();
        indexer.HoldScans = true;
        await sut.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        await settings.SaveAsync(WithRoot, TestContext.Current.CancellationToken).ConfigureAwait(true);

        await sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        int received = 0;
        messenger.Register<SettingsChangedMessage>(this, (_, _) => received++);
        await settings.SaveAsync(WithOtherRoot, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(indexer.ScanTokens[0].IsCancellationRequested);
        Assert.Equal(1, indexer.CallCount);
        Assert.Equal(0, received);
    }

    [Theory]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(System.IO.IOException))]
    [InlineData(typeof(TestDbException))]
    [InlineData(typeof(OperationCanceledException))]
    public async Task OnSettingsChanged_WhenTheRescanFails_TheNextChangeRescansAgain(Type failure)
    {
        (SettingsChangeBridge sut, FakeSettingsService settings, FakeIndexer indexer, _) = Create();
        indexer.ThrowOnNextCall = Assert.IsAssignableFrom<Exception>(Activator.CreateInstance(failure, "simuliert"));
        await sut.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        await settings.SaveAsync(WithRoot, TestContext.Current.CancellationToken).ConfigureAwait(true);
        await settings.SaveAsync(WithOtherRoot, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(2, indexer.CallCount);
        await sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private static (SettingsChangeBridge Sut, FakeSettingsService Settings, FakeIndexer Indexer, StrongReferenceMessenger Messenger) Create(
        AppSettings? initial = null)
    {
        FakeSettingsService settings = new(initial ?? AppSettings.Default);
        FakeIndexer indexer = new();
        StrongReferenceMessenger messenger = new();
        SettingsChangeBridge sut = new(settings, messenger, indexer, NullLogger<SettingsChangeBridge>.Instance);
        return (sut, settings, indexer, messenger);
    }
}
