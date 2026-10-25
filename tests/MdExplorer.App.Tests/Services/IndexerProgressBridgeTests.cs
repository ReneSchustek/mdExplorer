using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.Services;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Abstractions;
using MdExplorer.Indexer.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Sichert die Brücke zwischen <see cref="IIndexer.InitialScanProgress"/> und
/// <see cref="AllFilesViewModel.RefreshAsync"/> ab: Subscription-Lifecycle,
/// Dispatcher-Marshalling, Fehler-Toleranz und ein frischer Endstand.
/// </summary>
public sealed class IndexerProgressBridgeTests
{
    private const string Root = @"C:\Wurzel";
    private static readonly Guid FileId = new("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime FixedUtc = new(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);

    /// <remarks>
    /// Das Ereignis kommt vom Thread des Indexers. Ein synchrones <c>Invoke</c> hielte ihn an,
    /// bis die Oberfläche fertig ist, und beim Beenden wartet der UI-Thread auf den Indexer.
    /// Die Brücke stellt den Auftrag deshalb nur an.
    /// </remarks>
    [Fact]
    public async Task InitialScanProgress_AfterStart_PostsTheRefreshWithoutBlockingTheIndexer()
    {
        FakeAllFilesQuery query = new();
        AllFilesViewModel allFiles = NewAllFiles(query);
        FakeIndexer indexer = new();
        ImmediateUiDispatcher dispatcher = new();
        using IndexerProgressBridge sut = NewBridge(indexer, allFiles, dispatcher);

        await sut.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        indexer.RaiseProgress(Root, processedCount: 5, isCompleted: false);
        await IdleAsync(allFiles).ConfigureAwait(true);

        Assert.Equal(1, dispatcher.PostCount);
        Assert.Equal(0, dispatcher.InvokeCount);
        Assert.Equal(1, query.CallCount);
    }

    [Fact]
    public async Task InitialScanProgress_AfterStopAsync_TriggersNoRefresh()
    {
        FakeAllFilesQuery query = new();
        AllFilesViewModel allFiles = NewAllFiles(query);
        FakeIndexer indexer = new();
        ImmediateUiDispatcher dispatcher = new();
        using IndexerProgressBridge sut = NewBridge(indexer, allFiles, dispatcher);

        await sut.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        await sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        indexer.RaiseProgress(Root, processedCount: 5, isCompleted: false);

        Assert.Equal(0, dispatcher.PostCount);
        Assert.Equal(0, query.CallCount);
    }

    [Fact]
    public async Task Dispose_AfterStart_UnsubscribesFromTheIndexerEvent()
    {
        FakeAllFilesQuery query = new();
        AllFilesViewModel allFiles = NewAllFiles(query);
        FakeIndexer indexer = new();
        ImmediateUiDispatcher dispatcher = new();
        using IndexerProgressBridge sut = NewBridge(indexer, allFiles, dispatcher);

        await sut.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        sut.Dispose();
        indexer.RaiseProgress(Root, processedCount: 5, isCompleted: false);

        Assert.Equal(0, query.CallCount);
    }

    [Fact]
    public async Task Dispose_CalledTwice_StaysUnsubscribedWithoutError()
    {
        FakeAllFilesQuery query = new();
        AllFilesViewModel allFiles = NewAllFiles(query);
        FakeIndexer indexer = new();
        ImmediateUiDispatcher dispatcher = new();
        using IndexerProgressBridge sut = NewBridge(indexer, allFiles, dispatcher);
        await sut.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Exception? failure = Record.Exception(() =>
        {
            sut.Dispose();
            sut.Dispose();
        });
        indexer.RaiseProgress(Root, processedCount: 5, isCompleted: false);

        Assert.Null(failure);
        Assert.Equal(0, query.CallCount);
    }

    [Fact]
    public async Task InitialScanProgress_WhenRefreshThrows_SwallowsAndRemainsSubscribed()
    {
        FakeAllFilesQuery query = new() { ThrowOnNextCall = new InvalidCastException("kaputt") };
        AllFilesViewModel allFiles = NewAllFiles(query);
        FakeIndexer indexer = new();
        ImmediateUiDispatcher dispatcher = new();
        using IndexerProgressBridge sut = NewBridge(indexer, allFiles, dispatcher);

        await sut.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        indexer.RaiseProgress(Root, processedCount: 1, isCompleted: false);
        await IdleAsync(allFiles).ConfigureAwait(true);

        Assert.Equal(1, query.CallCount);
        Assert.False(allFiles.IsBusy);

        // Zweites Event muss trotz vorherigem Fehler durchgehen; Bridge bleibt subscribed.
        indexer.RaiseProgress(Root, processedCount: 2, isCompleted: false);
        await IdleAsync(allFiles).ConfigureAwait(true);

        Assert.Equal(2, query.CallCount);
    }

    /// <remarks>
    /// Kommt die letzte Meldung des Durchlaufs, während die Liste noch lädt, darf sie nicht
    /// verloren gehen: Sonst bliebe die Liste auf einem Zwischenstand stehen, bis irgendetwas
    /// anderes sie neu lädt. Aus mehreren Meldungen während eines Ladens wird ein Nachladen.
    /// </remarks>
    [Fact]
    public async Task InitialScanProgress_DuringARefresh_EndsWithOneMoreRefreshForTheFinalState()
    {
        TaskCompletionSource<bool> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeAllFilesQuery query = new() { Gate = gate };
        AllFilesViewModel allFiles = NewAllFiles(query);
        FakeIndexer indexer = new();
        ImmediateUiDispatcher dispatcher = new();
        using IndexerProgressBridge sut = NewBridge(indexer, allFiles, dispatcher);

        await sut.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        indexer.RaiseProgress(Root, processedCount: 1, isCompleted: false);
        Assert.True(allFiles.IsBusy);

        indexer.RaiseProgress(Root, processedCount: 2, isCompleted: false);
        indexer.RaiseProgress(Root, processedCount: 3, isCompleted: true);
        Assert.Equal(1, query.CallCount);

        gate.SetResult(true);
        await IdleAsync(allFiles).ConfigureAwait(true);

        Assert.Equal(2, query.CallCount);
        Assert.False(allFiles.IsBusy);
    }

    private static IndexerProgressBridge NewBridge(IIndexer indexer, AllFilesViewModel allFiles, IUiDispatcher dispatcher)
    {
        return new IndexerProgressBridge(indexer, allFiles, dispatcher, NullLogger<IndexerProgressBridge>.Instance);
    }

    private static AllFilesViewModel NewAllFiles(IAllFilesQuery query)
    {
        ServiceCollection services = new();
        _ = services.AddScoped(_ => query);
        ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        return new AllFilesViewModel(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<AllFilesViewModel>.Instance);
    }

    /// <summary>Wartet, bis die Liste nicht mehr lädt; das Signal kommt von der Liste selbst.</summary>
    /// <remarks>
    /// Die Brücke startet das Laden, ohne darauf zu warten. Statt in einer Schleife nachzusehen,
    /// hört der Test auf die Änderung von <see cref="AllFilesViewModel.IsBusy"/>. Die fünf
    /// Sekunden sind nur der Notausstieg, falls das Signal nie kommt.
    /// </remarks>
    private static Task IdleAsync(AllFilesViewModel allFiles)
    {
        TaskCompletionSource idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        allFiles.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AllFilesViewModel.IsBusy) && !allFiles.IsBusy)
            {
                _ = idle.TrySetResult();
            }
        };
        if (!allFiles.IsBusy)
        {
            _ = idle.TrySetResult();
        }
        return idle.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Liefert eine Zeile; kann beim ersten Abruf an einem Tor warten, um ein laufendes Laden
    /// festzuhalten, oder einmal scheitern.
    /// </summary>
    private sealed class FakeAllFilesQuery : IAllFilesQuery
    {
        public int CallCount { get; private set; }

        public Exception? ThrowOnNextCall { get; set; }

        public TaskCompletionSource<bool>? Gate { get; set; }

        public async Task<IReadOnlyList<AllFilesRow>> GetAllAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            TaskCompletionSource<bool>? gate = Gate;
            Gate = null;
            if (gate is not null)
            {
                _ = await gate.Task.ConfigureAwait(false);
            }
            if (ThrowOnNextCall is { } pending)
            {
                ThrowOnNextCall = null;
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(pending).Throw();
            }
            return [new AllFilesRow(FileId, "Stub", "stub.md", @$"{Root}\stub.md", FixedUtc, [])];
        }
    }
}
