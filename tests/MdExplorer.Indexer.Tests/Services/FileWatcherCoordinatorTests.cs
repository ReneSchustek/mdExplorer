using MdExplorer.Indexer.Models;
using MdExplorer.Indexer.Options;
using MdExplorer.Indexer.Services;
using MdExplorer.Indexer.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.Indexer.Tests.Services;

/// <summary>
/// Der Koordinator fasst schnelle Ereignisse je Pfad zu einem zusammen und gibt es erst nach der
/// Entprellzeit frei; nach dem Anhalten schreibt er nichts mehr in den Kanal. Ohne Entprellung
/// liest der Indexer eine Datei mitten im Speichern mehrfach, ohne Sperre nach dem Anhalten
/// scheitert ein später Zeitgeber am geschlossenen Kanal.
/// </summary>
public sealed class FileWatcherCoordinatorTests
{
    private const string Root = @"C:\Wurzel";
    private const string FilePath = @"C:\Wurzel\datei.md";

    [Fact]
    public async Task OnRawEvent_SingleEventAfterTheDebounce_WritesToChannel()
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
    public async Task OnRawEvent_RapidChanges_DebouncesToSingleLatestEvent()
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
    public async Task OnRawEvent_ChangeRightAfterARename_KeepsTheRename()
    {
        // Viele Editoren speichern über eine Umbenennung und schreiben danach in die neue Datei.
        // Gewänne das Changed, sähe der Indexer eine neue Datei, und der alte Eintrag bliebe als
        // Waise stehen.
        const string oldPath = @"C:\Wurzel\alt.md";
        FakeFileWatcherFactory factory = new();
        FakeTimeProvider time = new();
        IndexerOptions options = new() { DebounceMs = 300 };
        FileWatcherCoordinator sut = new(factory, options.ToOptions(), time, NullLogger<FileWatcherCoordinator>.Instance);
        await using (sut.ConfigureAwait(true))
        {
            sut.Start([Root]);

            factory.Watchers[Root].TriggerEvent(new FileSystemEvent(FileSystemEventKind.Renamed, FilePath, oldPath, Root));
            time.Advance(TimeSpan.FromMilliseconds(100));
            factory.Watchers[Root].TriggerEvent(new FileSystemEvent(FileSystemEventKind.Changed, FilePath, OldPath: null, Root));
            time.Advance(TimeSpan.FromMilliseconds(300));

            Assert.True(sut.Events.TryRead(out FileSystemEvent? consumed));
            Assert.NotNull(consumed);
            Assert.Equal(FileSystemEventKind.Renamed, consumed.Kind);
            Assert.Equal(oldPath, consumed.OldPath);
            Assert.False(sut.Events.TryRead(out _));
        }
    }

    [Fact]
    public async Task OnRawEvent_BeforeTheDebounceElapses_DoesNotWrite()
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
    public async Task StopAsync_AfterStart_ClosesChannelAndDisposesWatchers()
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
    public async Task OnRawEvent_MultipleDistinctPaths_EmitsOnePerPath()
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
            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.NotEqual(first.Path, second.Path);
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
    public async Task Start_OnABlankRoot_CreatesNoWatcher(string empty)
    {
        FakeFileWatcherFactory factory = new();
        FakeTimeProvider time = new();
        IndexerOptions options = new() { DebounceMs = 300 };
        FileWatcherCoordinator sut = new(factory, options.ToOptions(), time, NullLogger<FileWatcherCoordinator>.Instance);
        await using (sut.ConfigureAwait(true))
        {
            sut.Start([empty, Root]);

            _ = Assert.Single(factory.Watchers);
            Assert.True(factory.Watchers.ContainsKey(Root));
        }
    }

    /// <remarks>
    /// Nach dem Freigeben darf kein Start mehr durchgehen, sonst hinge ein Wächter an einem
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
    /// Zweimal freigeben ist harmlos. Der zweite Aufruf kommt vom Container, der erste vom
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
    /// heruntergefahren wird. Jeder Wecker der Entprellung muss beim Anhalten freigegeben werden;
    /// sonst liefe er danach ab und schriebe in einen geschlossenen Kanal.
    /// </remarks>
    [Fact]
    public async Task StopAsync_WhileADebounceIsPending_DisposesItsTimer()
    {
        FakeFileWatcherFactory factory = new();
        SignalingTimeProvider time = new();
        FileWatcherCoordinator sut = Create(factory, time);
        await using (sut.ConfigureAwait(true))
        {
            sut.Start([Root]);
            factory.Watchers[Root].TriggerEvent(new FileSystemEvent(FileSystemEventKind.Changed, FilePath, OldPath: null, Root));

            await sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            SignalingTimeProvider.RecordedTimer timer = Assert.Single(time.Timers);
            Assert.True(timer.IsDisposed);
        }
    }

    /// <remarks>
    /// Und die Gegenrichtung: Ein Ereignis, das der Watcher schon auf einem Pool-Thread zustellt,
    /// während der Koordinator anhält, darf gar nicht erst vorgemerkt werden.
    /// </remarks>
    [Fact]
    public async Task OnRawEvent_ArrivingAfterStop_IsIgnored()
    {
        FakeFileWatcherFactory factory = new();
        SignalingTimeProvider time = new();
        FileWatcherCoordinator sut = Create(factory, time);
        await using (sut.ConfigureAwait(true))
        {
            sut.Start([Root]);
            await sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            factory.Watchers[Root].TriggerEventInFlight(new FileSystemEvent(FileSystemEventKind.Changed, FilePath, OldPath: null, Root));

            Assert.Empty(time.Timers);
        }
    }

    [Fact]
    public async Task OnRawEvent_RenameChainWithinTheDebounce_EmitsOneRenameFromTheFirstName()
    {
        // a → b → c innerhalb der Entprellung. Kämen zwei Umbenennungen getrennt an, könnte b → c
        // vor a → b verarbeitet werden; der Eintrag von a ginge verloren und c bekäme eine neue Id.
        FakeFileWatcherFactory factory = new();
        SignalingTimeProvider time = new();
        FileWatcherCoordinator sut = Create(factory, time);
        await using (sut.ConfigureAwait(true))
        {
            sut.Start([Root]);
            factory.Watchers[Root].TriggerEvent(new FileSystemEvent(FileSystemEventKind.Renamed, @"C:\Wurzel\b.md", @"C:\Wurzel\a.md", Root));
            factory.Watchers[Root].TriggerEvent(new FileSystemEvent(FileSystemEventKind.Renamed, @"C:\Wurzel\c.md", @"C:\Wurzel\b.md", Root));
            time.Advance(TimeSpan.FromMilliseconds(300));

            Assert.True(sut.Events.TryRead(out FileSystemEvent? consumed));
            Assert.Equal(new FileSystemEvent(FileSystemEventKind.Renamed, @"C:\Wurzel\c.md", @"C:\Wurzel\a.md", Root), consumed);
            Assert.False(sut.Events.TryRead(out _));
        }
    }

    [Fact]
    public async Task OnRawEvent_SecondRenameOntoTheSameTarget_EmitsBothRenames()
    {
        // Zwei Umbenennungen auf dasselbe Ziel: Ging die erste in der zweiten auf, verlöre sie ihren
        // alten Pfad, und dessen Eintrag bliebe als Waise stehen.
        FakeFileWatcherFactory factory = new();
        SignalingTimeProvider time = new();
        FileWatcherCoordinator sut = Create(factory, time);
        await using (sut.ConfigureAwait(true))
        {
            sut.Start([Root]);
            factory.Watchers[Root].TriggerEvent(new FileSystemEvent(FileSystemEventKind.Renamed, FilePath, @"C:\Wurzel\erste.md", Root));
            factory.Watchers[Root].TriggerEvent(new FileSystemEvent(FileSystemEventKind.Renamed, FilePath, @"C:\Wurzel\zweite.md", Root));
            time.Advance(TimeSpan.FromMilliseconds(300));

            List<string?> oldPaths = [];
            while (sut.Events.TryRead(out FileSystemEvent? consumed))
            {
                oldPaths.Add(consumed.OldPath);
            }
            Assert.Equal([@"C:\Wurzel\erste.md", @"C:\Wurzel\zweite.md"], oldPaths);
        }
    }

    [Theory]
    [InlineData(FileSystemEventKind.ResyncRequested)]
    [InlineData(FileSystemEventKind.DirectoryRenamed)]
    [InlineData(FileSystemEventKind.DirectoryDeleted)]
    [InlineData(FileSystemEventKind.IgnoreFileChanged)]
    public async Task OnRawEvent_StructuralEvent_PassesItOnWithoutDebounce(FileSystemEventKind kind)
    {
        // Ordnerereignisse, Pufferüberlauf und geänderte .mdignore betreffen viele Dateien auf einmal.
        // Sie gehen sofort weiter, damit spätere Dateiereignisse im neuen Ordner nicht vor ihnen ankommen.
        FakeFileWatcherFactory factory = new();
        SignalingTimeProvider time = new();
        FileWatcherCoordinator sut = Create(factory, time);
        await using (sut.ConfigureAwait(true))
        {
            sut.Start([Root]);

            factory.Watchers[Root].TriggerEvent(new FileSystemEvent(kind, @"C:\Wurzel\ordner", @"C:\Wurzel\alt", Root));

            Assert.True(sut.Events.TryRead(out FileSystemEvent? consumed));
            Assert.Equal(kind, consumed.Kind);
        }
    }

    [Fact]
    public async Task Start_WhenOneWatcherCannotStart_StartsTheOthersAndFreesTheBrokenOne()
    {
        FakeFileWatcherFactory factory = new();
        _ = factory.FailingRoots.Add(@"C:\Netz");
        SignalingTimeProvider time = new();
        FileWatcherCoordinator sut = Create(factory, time);
        await using (sut.ConfigureAwait(true))
        {
            sut.Start([@"C:\Netz", Root]);

            Assert.True(factory.Watchers[Root].IsStarted);
            Assert.True(factory.Watchers[@"C:\Netz"].IsDisposed);
        }
    }

    private static FileWatcherCoordinator Create(FakeFileWatcherFactory factory, SignalingTimeProvider time)
    {
        IndexerOptions options = new() { DebounceMs = 300 };
        return new FileWatcherCoordinator(factory, options.ToOptions(), time, NullLogger<FileWatcherCoordinator>.Instance);
    }
}
