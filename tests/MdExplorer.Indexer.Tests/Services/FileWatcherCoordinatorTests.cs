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

        await sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

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
    /// <remarks>
    /// Zweimal starten ist ein Programmierfehler, kein Betriebszustand: Beim zweiten Lauf
    /// hingen zwei Wächter am selben Baum, und jede Änderung käme doppelt an. Deshalb bricht
    /// der Vorgang ab, statt sich zu arrangieren.
    /// </remarks>
    [Fact]
    public async Task Start_CalledTwice_Throws()
    {
        FakeFileWatcherFactory factory = new();
        FakeTimeProvider time = new();
        IndexerOptions options = new() { DebounceMs = 300 };
        FileWatcherCoordinator sut = new(factory, options.ToOptions(), time, NullLogger<FileWatcherCoordinator>.Instance);
        await using (sut.ConfigureAwait(true))
        {
            sut.Start([Root]);

            _ = Assert.Throws<InvalidOperationException>(() => sut.Start([Root]));
        }
    }

    /// <remarks>
    /// Eine leere Wurzel steht in den Einstellungen, sobald jemand eine Zeile angefangen und
    /// wieder gelöscht hat. Daraus darf kein Wächter auf dem Arbeitsverzeichnis werden.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Start_OnABlankRoot_CreatesNoWatcher(string leer)
    {
        FakeFileWatcherFactory factory = new();
        FakeTimeProvider time = new();
        IndexerOptions options = new() { DebounceMs = 300 };
        FileWatcherCoordinator sut = new(factory, options.ToOptions(), time, NullLogger<FileWatcherCoordinator>.Instance);
        await using (sut.ConfigureAwait(true))
        {
            sut.Start([leer, Root]);

            _ = Assert.Single(factory.Watchers);
            Assert.True(factory.Watchers.ContainsKey(Root));
        }
    }

    /// <remarks>
    /// Nach dem Freigeben darf kein Start mehr durchgehen — sonst hinge ein Wächter an einem
    /// Kanal, den niemand mehr liest.
    /// </remarks>
    [Fact]
    public async Task Start_AfterDispose_Throws()
    {
        FakeFileWatcherFactory factory = new();
        FakeTimeProvider time = new();
        IndexerOptions options = new() { DebounceMs = 300 };
        FileWatcherCoordinator sut = new(factory, options.ToOptions(), time, NullLogger<FileWatcherCoordinator>.Instance);
        await sut.DisposeAsync().ConfigureAwait(true);

        _ = Assert.Throws<ObjectDisposedException>(() => sut.Start([Root]));
    }

    /// <remarks>
    /// Zweimal freigeben ist harmlos — der zweite Aufruf kommt vom Container, der erste vom
    /// ordentlichen Herunterfahren.
    /// </remarks>
    [Fact]
    public async Task DisposeAsync_CalledTwice_IsHarmless()
    {
        FakeFileWatcherFactory factory = new();
        FakeTimeProvider time = new();
        IndexerOptions options = new() { DebounceMs = 300 };
        FileWatcherCoordinator sut = new(factory, options.ToOptions(), time, NullLogger<FileWatcherCoordinator>.Instance);

        await sut.DisposeAsync().ConfigureAwait(true);
        await sut.DisposeAsync().ConfigureAwait(true);
    }

    /// <remarks>
    /// Der unangenehme Zeitpunkt: Der Wecker für eine Änderung läuft noch, während der Dienst
    /// heruntergefahren wird. Läuft er danach ab, darf er nicht mehr in einen geschlossenen
    /// Kanal schreiben.
    /// </remarks>
    [Fact]
    public async Task OnDebounceFiringAfterStop_WritesNothing()
    {
        FakeFileWatcherFactory factory = new();
        FakeTimeProvider time = new();
        IndexerOptions options = new() { DebounceMs = 300 };
        FileWatcherCoordinator sut = new(factory, options.ToOptions(), time, NullLogger<FileWatcherCoordinator>.Instance);
        await using (sut.ConfigureAwait(true))
        {
            sut.Start([Root]);
            factory.Watchers[Root].TriggerEvent(new FileSystemEvent(FileSystemEventKind.Changed, FilePath, OldPath: null, Root));

            await sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            time.Advance(TimeSpan.FromMilliseconds(300));

            Assert.False(sut.Events.TryRead(out _));
        }
    }

    /// <remarks>
    /// Und die Gegenrichtung: Eine Änderung, die **nach** dem Anhalten hereinkommt, darf gar
    /// nicht erst vorgemerkt werden.
    /// </remarks>
    [Fact]
    public async Task OnEventAfterStop_IsIgnored()
    {
        FakeFileWatcherFactory factory = new();
        FakeTimeProvider time = new();
        IndexerOptions options = new() { DebounceMs = 300 };
        FileWatcherCoordinator sut = new(factory, options.ToOptions(), time, NullLogger<FileWatcherCoordinator>.Instance);
        await using (sut.ConfigureAwait(true))
        {
            sut.Start([Root]);
            await sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            factory.Watchers[Root].TriggerEvent(new FileSystemEvent(FileSystemEventKind.Changed, FilePath, OldPath: null, Root));
            time.Advance(TimeSpan.FromMilliseconds(300));

            Assert.False(sut.Events.TryRead(out _));
        }
    }
}
