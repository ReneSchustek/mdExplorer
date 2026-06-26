using MdExplorer.App.Services;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Abstractions;
using MdExplorer.Indexer.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Sichert die Brücke zwischen <see cref="IIndexer.InitialScanProgress"/> und
/// <see cref="AllFilesViewModel.RefreshAsync"/> ab — Subscription-Lifecycle,
/// Dispatcher-Marshalling, Fehler-Toleranz und IsBusy-Guard.
/// </summary>
public sealed class IndexerProgressBridgeTests
{
    private const string Root = @"C:\Wurzel";
    private static readonly Guid FileId = new("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime FixedUtc = new(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task OnProgressAfterStart_InvokesAllFilesRefreshOnDispatcher()
    {
        FakeAllFilesQuery query = new();
        AllFilesViewModel allFiles = NewAllFiles(query);
        FakeIndexer indexer = new();
        ImmediateUiDispatcher dispatcher = new();
        using IndexerProgressBridge sut = NewBridge(indexer, allFiles, dispatcher);

        await sut.StartAsync(CancellationToken.None).ConfigureAwait(true);
        indexer.Raise(new IndexerScanProgressEventArgs(Root, processedCount: 5, isCompleted: false));
        await WaitForIdleAsync(allFiles).ConfigureAwait(true);

        Assert.Equal(1, dispatcher.InvokeCount);
        Assert.Equal(1, query.CallCount);
    }

    [Fact]
    public async Task AfterStopAsync_FurtherEventsDoNotTriggerRefresh()
    {
        FakeAllFilesQuery query = new();
        AllFilesViewModel allFiles = NewAllFiles(query);
        FakeIndexer indexer = new();
        ImmediateUiDispatcher dispatcher = new();
        using IndexerProgressBridge sut = NewBridge(indexer, allFiles, dispatcher);

        await sut.StartAsync(CancellationToken.None).ConfigureAwait(true);
        await sut.StopAsync(CancellationToken.None).ConfigureAwait(true);
        indexer.Raise(new IndexerScanProgressEventArgs(Root, processedCount: 5, isCompleted: false));

        Assert.Equal(0, dispatcher.InvokeCount);
        Assert.Equal(0, query.CallCount);
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromIndexerEvent()
    {
        FakeAllFilesQuery query = new();
        AllFilesViewModel allFiles = NewAllFiles(query);
        FakeIndexer indexer = new();
        ImmediateUiDispatcher dispatcher = new();
        using IndexerProgressBridge sut = NewBridge(indexer, allFiles, dispatcher);

        await sut.StartAsync(CancellationToken.None).ConfigureAwait(true);
        sut.Dispose();
        indexer.Raise(new IndexerScanProgressEventArgs(Root, processedCount: 5, isCompleted: false));

        Assert.Equal(0, query.CallCount);
    }

    [Fact]
    public async Task Dispose_IsIdempotent_DoesNotThrow()
    {
        FakeAllFilesQuery query = new();
        AllFilesViewModel allFiles = NewAllFiles(query);
        FakeIndexer indexer = new();
        ImmediateUiDispatcher dispatcher = new();
        using IndexerProgressBridge sut = NewBridge(indexer, allFiles, dispatcher);

        await sut.StartAsync(CancellationToken.None).ConfigureAwait(true);
        sut.Dispose();
        sut.Dispose();
    }

    [Fact]
    public async Task WhenRefreshThrows_BridgeSwallowsAndRemainsSubscribed()
    {
        FakeAllFilesQuery query = new() { ThrowOnNextCall = new InvalidCastException("kaputt") };
        AllFilesViewModel allFiles = NewAllFiles(query);
        FakeIndexer indexer = new();
        ImmediateUiDispatcher dispatcher = new();
        using IndexerProgressBridge sut = NewBridge(indexer, allFiles, dispatcher);

        await sut.StartAsync(CancellationToken.None).ConfigureAwait(true);
        indexer.Raise(new IndexerScanProgressEventArgs(Root, processedCount: 1, isCompleted: false));
        await WaitForIdleAsync(allFiles).ConfigureAwait(true);

        Assert.Equal(1, query.CallCount);
        Assert.False(allFiles.IsBusy);

        // Zweites Event muss trotz vorherigem Fehler durchgehen — Bridge bleibt subscribed.
        indexer.Raise(new IndexerScanProgressEventArgs(Root, processedCount: 2, isCompleted: false));
        await WaitForIdleAsync(allFiles).ConfigureAwait(true);

        Assert.Equal(2, query.CallCount);
    }

    [Fact]
    public async Task TwoEventsInQuickSuccession_OnlyTriggerSingleRefresh_DueToIsBusyGuard()
    {
        FakeAllFilesQuery query = new() { Gate = new TaskCompletionSource<bool>() };
        AllFilesViewModel allFiles = NewAllFiles(query);
        FakeIndexer indexer = new();
        ImmediateUiDispatcher dispatcher = new();
        using IndexerProgressBridge sut = NewBridge(indexer, allFiles, dispatcher);

        await sut.StartAsync(CancellationToken.None).ConfigureAwait(true);
        indexer.Raise(new IndexerScanProgressEventArgs(Root, processedCount: 1, isCompleted: false));
        // Erster Refresh hängt am Gate → IsBusy=true; zweites Event muss durchfallen.
        Assert.True(allFiles.IsBusy);
        Assert.Equal(1, query.CallCount);

        indexer.Raise(new IndexerScanProgressEventArgs(Root, processedCount: 2, isCompleted: false));
        Assert.Equal(1, query.CallCount);

        query.Gate.SetResult(true);
        await WaitForIdleAsync(allFiles).ConfigureAwait(true);

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
        return new AllFilesViewModel(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AllFilesViewModel>.Instance);
    }

    private static async Task WaitForIdleAsync(AllFilesViewModel allFiles)
    {
        // Bridge feuert RefreshAsync fire-and-forget — abwarten bis IsBusy zurückfällt.
        TimeSpan timeout = TimeSpan.FromSeconds(2);
        DateTime deadline = DateTime.UtcNow + timeout;
        while (allFiles.IsBusy && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5).ConfigureAwait(true);
        }
    }

    private sealed class FakeIndexer : IIndexer
    {
        public event EventHandler<IndexerScanProgressEventArgs>? InitialScanProgress;

        public Task RunInitialScanAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Raise(IndexerScanProgressEventArgs args) =>
            InitialScanProgress?.Invoke(this, args);
    }

    private sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public int InvokeCount { get; private set; }

        public void Invoke(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            InvokeCount++;
            action();
        }
    }

    private sealed class FakeAllFilesQuery : IAllFilesQuery
    {
        public int CallCount { get; private set; }

        public Exception? ThrowOnNextCall { get; set; }

        public TaskCompletionSource<bool>? Gate { get; set; }

        public async Task<IReadOnlyList<AllFilesRow>> GetAllAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            if (Gate is not null)
            {
                _ = await Gate.Task.ConfigureAwait(false);
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
