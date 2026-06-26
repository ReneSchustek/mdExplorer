using MdExplorer.Indexer.Models;
using MdExplorer.Indexer.Options;
using MdExplorer.Indexer.Services;
using MdExplorer.Indexer.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.Indexer.Tests.Services;

public sealed class FileWatcherCoordinatorTests
{
    private const string Root = @"C:\Wurzel";
    private const string FilePath = @"C:\Wurzel\datei.md";

    [Fact]
    public async Task OnSingleEvent_AfterDebounceElapsed_WritesToChannel()
    {
        FakeFileWatcherFactory factory = new();
        FakeTimeProvider time = new();
        IndexerOptions options = new() { DebounceMs = 300 };
        FileWatcherCoordinator sut = new(factory, options.ToOptions(), time, NullLogger<FileWatcherCoordinator>.Instance);
        await using (sut.ConfigureAwait(true))
        {
            sut.Start([Root]);

            factory.Watchers[Root].TriggerEvent(new FileSystemEvent(FileSystemEventKind.Created, FilePath, OldPath: null, Root));
            time.Advance(TimeSpan.FromMilliseconds(300));

            Assert.True(sut.Events.TryRead(out FileSystemEvent? consumed));
            Assert.NotNull(consumed);
            Assert.Equal(FileSystemEventKind.Created, consumed.Kind);
            Assert.Equal(FilePath, consumed.Path);
        }
    }

    [Fact]
    public async Task OnRapidChanges_DebouncesToSingleLatestEvent()
    {
        FakeFileWatcherFactory factory = new();
        FakeTimeProvider time = new();
        IndexerOptions options = new() { DebounceMs = 300 };
        FileWatcherCoordinator sut = new(factory, options.ToOptions(), time, NullLogger<FileWatcherCoordinator>.Instance);
        await using (sut.ConfigureAwait(true))
        {
            sut.Start([Root]);

            factory.Watchers[Root].TriggerEvent(new FileSystemEvent(FileSystemEventKind.Created, FilePath, OldPath: null, Root));
            time.Advance(TimeSpan.FromMilliseconds(100));
            factory.Watchers[Root].TriggerEvent(new FileSystemEvent(FileSystemEventKind.Changed, FilePath, OldPath: null, Root));
            time.Advance(TimeSpan.FromMilliseconds(100));
            factory.Watchers[Root].TriggerEvent(new FileSystemEvent(FileSystemEventKind.Changed, FilePath, OldPath: null, Root));
            time.Advance(TimeSpan.FromMilliseconds(300));

            Assert.True(sut.Events.TryRead(out FileSystemEvent? consumed));
            Assert.NotNull(consumed);
            Assert.Equal(FileSystemEventKind.Changed, consumed.Kind);
            Assert.False(sut.Events.TryRead(out _));
        }
    }

    [Fact]
    public async Task OnEventBeforeDebounce_DoesNotWrite()
    {
        FakeFileWatcherFactory factory = new();
        FakeTimeProvider time = new();
        IndexerOptions options = new() { DebounceMs = 300 };
        FileWatcherCoordinator sut = new(factory, options.ToOptions(), time, NullLogger<FileWatcherCoordinator>.Instance);
        await using (sut.ConfigureAwait(true))
        {
            sut.Start([Root]);

            factory.Watchers[Root].TriggerEvent(new FileSystemEvent(FileSystemEventKind.Changed, FilePath, OldPath: null, Root));
            time.Advance(TimeSpan.FromMilliseconds(150));

            Assert.False(sut.Events.TryRead(out _));
        }
    }

    [Fact]
    public async Task OnStop_ClosesChannelAndDisposesWatchers()
    {
        FakeFileWatcherFactory factory = new();
        FakeTimeProvider time = new();
        IndexerOptions options = new() { DebounceMs = 300 };
        FileWatcherCoordinator sut = new(factory, options.ToOptions(), time, NullLogger<FileWatcherCoordinator>.Instance);
        sut.Start([Root]);

        await sut.StopAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.True(factory.Watchers[Root].IsDisposed);
        Assert.True(sut.Events.Completion.IsCompleted);
        await sut.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task OnMultipleDistinctPaths_EmitsOnePerPath()
    {
        FakeFileWatcherFactory factory = new();
        FakeTimeProvider time = new();
        IndexerOptions options = new() { DebounceMs = 200 };
        FileWatcherCoordinator sut = new(factory, options.ToOptions(), time, NullLogger<FileWatcherCoordinator>.Instance);
        await using (sut.ConfigureAwait(true))
        {
            sut.Start([Root]);

            factory.Watchers[Root].TriggerEvent(new FileSystemEvent(FileSystemEventKind.Created, @"C:\Wurzel\a.md", OldPath: null, Root));
            factory.Watchers[Root].TriggerEvent(new FileSystemEvent(FileSystemEventKind.Created, @"C:\Wurzel\b.md", OldPath: null, Root));
            time.Advance(TimeSpan.FromMilliseconds(200));

            Assert.True(sut.Events.TryRead(out FileSystemEvent? first));
            Assert.True(sut.Events.TryRead(out FileSystemEvent? second));
            Assert.False(sut.Events.TryRead(out _));
            Assert.NotEqual(first!.Path, second!.Path);
        }
    }
}
