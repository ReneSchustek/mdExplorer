using System.IO;
using System.Text;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.Core.Tests.Settings;

public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public JsonSettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mdexp-settings-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Tests dürfen das Verzeichnis selbst entfernen.
        }
    }

    [Fact]
    public async Task LoadAsync_OnMissingFile_ReturnsDefaults()
    {
        string path = Path.Combine(_tempDir, "missing.json");
        using JsonSettingsService sut = new(path, NullLogger<JsonSettingsService>.Instance);

        AppSettings result = await sut.LoadAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(AppSettings.CurrentSchemaVersion, result.SchemaVersion);
        Assert.Empty(result.Indexing.Roots);
        Assert.Equal(IndexingSettings.DefaultExclusionPatterns, result.Indexing.ExclusionPatterns);
        Assert.Empty(result.Indexing.UiExcludedFolders);
        Assert.Equal(AppTheme.System, result.Appearance.Theme);
    }

    [Fact]
    public async Task LoadAsync_OnInvalidJson_ReturnsDefaultsAndDoesNotThrow()
    {
        string path = Path.Combine(_tempDir, "broken.json");
        await File.WriteAllTextAsync(path, "{ this is not valid json", Encoding.UTF8).ConfigureAwait(true);
        using JsonSettingsService sut = new(path, NullLogger<JsonSettingsService>.Instance);

        AppSettings result = await sut.LoadAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(AppSettings.CurrentSchemaVersion, result.SchemaVersion);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsValues()
    {
        string path = Path.Combine(_tempDir, "settings.json");
        AppSettings input = new(
            AppSettings.CurrentSchemaVersion,
            new IndexingSettings([@"C:\Notes"], ["**/Drafts/**", "!Drafts/keep.md"], [@"C:\Notes\Drafts\Archive"], false),
            new AppearanceSettings(AppTheme.Dark, 18, 75),
            new BehaviorSettings(450, 900));

        using JsonSettingsService writer = new(path, NullLogger<JsonSettingsService>.Instance);
        await writer.SaveAsync(input, CancellationToken.None).ConfigureAwait(true);

        using JsonSettingsService reader = new(path, NullLogger<JsonSettingsService>.Instance);
        AppSettings result = await reader.LoadAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(input.Indexing.Roots, result.Indexing.Roots);
        Assert.Equal(input.Indexing.ExclusionPatterns, result.Indexing.ExclusionPatterns);
        Assert.Equal(input.Indexing.UiExcludedFolders, result.Indexing.UiExcludedFolders);
        Assert.False(result.Indexing.AutoExtractHashtags);
        Assert.Equal(AppTheme.Dark, result.Appearance.Theme);
        Assert.Equal(18, result.Appearance.PreviewFontSize);
        Assert.Equal(75, result.Appearance.ResultsPerPage);
        Assert.Equal(450, result.Behavior.SearchDebounceMs);
        Assert.Equal(900, result.Behavior.IndexerResyncIntervalSeconds);
    }

    [Fact]
    public async Task SaveAsync_OnDifferingPayload_RaisesSettingsChanged()
    {
        string path = Path.Combine(_tempDir, "events.json");
        using JsonSettingsService sut = new(path, NullLogger<JsonSettingsService>.Instance);
        TaskCompletionSource<SettingsChangedEventArgs> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.SettingsChanged += (_, args) => received.TrySetResult(args);

        AppSettings target = AppSettings.Default with
        {
            Behavior = AppSettings.Default.Behavior with { SearchDebounceMs = 1000 },
        };

        await sut.SaveAsync(target, CancellationToken.None).ConfigureAwait(true);
        SettingsChangedEventArgs delivered = await received.Task
            .WaitAsync(TimeSpan.FromSeconds(5))
            .ConfigureAwait(true);

        Assert.Equal(300, delivered.Previous.Behavior.SearchDebounceMs);
        Assert.Equal(1000, delivered.Current.Behavior.SearchDebounceMs);
    }

    [Fact]
    public async Task SaveAsync_OnIdenticalPayload_DoesNotRaiseEvent()
    {
        string path = Path.Combine(_tempDir, "noop.json");
        using JsonSettingsService sut = new(path, NullLogger<JsonSettingsService>.Instance);
        AppSettings target = AppSettings.Default;
        await sut.SaveAsync(target, CancellationToken.None).ConfigureAwait(true);

        int handlerCount = 0;
        sut.SettingsChanged += (_, _) => Interlocked.Increment(ref handlerCount);

        await sut.SaveAsync(target, CancellationToken.None).ConfigureAwait(true);
        // Falls die Implementierung das Event versehentlich dennoch asynchron postet,
        // braucht der ThreadPool/Sync-Context eine kurze Karenzzeit, um es zuzustellen.
        await Task.Delay(50).ConfigureAwait(true);

        Assert.Equal(0, handlerCount);
    }

    [Fact]
    public async Task SaveAsync_OnCapturedSyncContext_PostsSettingsChangedThroughContext()
    {
        string path = Path.Combine(_tempDir, "sync.json");
        using JsonSettingsService sut = new(path, NullLogger<JsonSettingsService>.Instance);
        QueueingSynchronizationContext context = new();
        int handlerInvocations = 0;
        sut.SettingsChanged += (_, _) => Interlocked.Increment(ref handlerInvocations);

        AppSettings target = AppSettings.Default with
        {
            Behavior = AppSettings.Default.Behavior with { SearchDebounceMs = 555 },
        };

        // Save auf dediziertem Worker-Thread starten und den Sync-Context dort als
        // Aufrufer-Context setzen. Damit landet der Post() in der Queue (nicht auf dem Test-Thread),
        // und das Test-`await` muss nicht selbst durch unseren Context tunneln.
        await Task.Run(async () =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                await sut.SaveAsync(target, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
            }
        }).ConfigureAwait(true);

        Assert.Equal(1, context.PostCount);
        Assert.Equal(0, handlerInvocations);
        context.DrainPending();
        Assert.Equal(1, handlerInvocations);
    }

    [Fact]
    public async Task SaveAsync_OnThreeDistinctSaves_WritesThreeSnapshotsAndThreeAuditLines()
    {
        string path = Path.Combine(_tempDir, "audited.json");
        string historyDir = Path.Combine(_tempDir, "audited-history");
        string auditLog = Path.Combine(_tempDir, "audited-audit.log");
        _ = Directory.CreateDirectory(historyDir);

        FakeTimeProvider clock = new(new DateTimeOffset(2026, 06, 10, 12, 0, 0, TimeSpan.Zero));
        using FileSystemSettingsHistoryStore history = new(
            historyDir,
            auditLog,
            FileSystemSettingsHistoryStore.DefaultRetention,
            NullLogger<FileSystemSettingsHistoryStore>.Instance);
        using JsonSettingsService sut = new(
            path,
            NullLogger<JsonSettingsService>.Instance,
            history,
            clock);

        AppSettings stand1 = AppSettings.Default with
        {
            Behavior = AppSettings.Default.Behavior with { SearchDebounceMs = 400 },
        };
        await sut.SaveAsync(stand1, CancellationToken.None).ConfigureAwait(true);

        clock.Advance(TimeSpan.FromSeconds(2));
        AppSettings stand2 = stand1 with
        {
            Appearance = stand1.Appearance with { Theme = AppTheme.Dark },
        };
        await sut.SaveAsync(stand2, CancellationToken.None).ConfigureAwait(true);

        clock.Advance(TimeSpan.FromSeconds(3));
        AppSettings stand3 = stand2 with
        {
            Indexing = stand2.Indexing with { Roots = [@"C:\Notes"] },
        };
        await sut.SaveAsync(stand3, CancellationToken.None).ConfigureAwait(true);

        string[] snapshots = Directory.GetFiles(historyDir, "settings.*.json");
        Assert.Equal(3, snapshots.Length);

        string[] auditLines = await File.ReadAllLinesAsync(auditLog).ConfigureAwait(true);
        Assert.Equal(3, auditLines.Length);
        Assert.Contains("\"behavior.searchDebounceMs\"", auditLines[0], StringComparison.Ordinal);
        Assert.Contains("\"appearance.theme\"", auditLines[1], StringComparison.Ordinal);
        Assert.Contains("\"indexing.roots[0]\"", auditLines[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAsync_OnContentEquivalentButFreshListInstance_DoesNotTreatAsChange()
    {
        // Regression: AppSettings ist ein Record; IReadOnlyList<string>-Felder fallen beim
        // Default-Equality auf Reference-Equality zurueck. Der Settings-Dialog erzeugt bei
        // jedem OK-Klick neue Listen-Instanzen — ohne strukturellen Vergleich wuerde der
        // Dienst jedes Save als geaendert melden und unnoetigen Snapshot / SettingsChanged-Event
        // ausloesen (Befund 2026-06-10).
        string path = Path.Combine(_tempDir, "fresh-list.json");
        string historyDir = Path.Combine(_tempDir, "fresh-list-history");
        string auditLog = Path.Combine(_tempDir, "fresh-list-audit.log");
        _ = Directory.CreateDirectory(historyDir);

        FakeTimeProvider clock = new(new DateTimeOffset(2026, 06, 10, 12, 0, 0, TimeSpan.Zero));
        using FileSystemSettingsHistoryStore history = new(
            historyDir,
            auditLog,
            FileSystemSettingsHistoryStore.DefaultRetention,
            NullLogger<FileSystemSettingsHistoryStore>.Instance);
        using JsonSettingsService sut = new(
            path,
            NullLogger<JsonSettingsService>.Instance,
            history,
            clock);

        AppSettings first = AppSettings.Default with
        {
            Indexing = AppSettings.Default.Indexing with { Roots = [@"F:\Notes"] },
        };
        await sut.SaveAsync(first, CancellationToken.None).ConfigureAwait(true);

        int eventCount = 0;
        sut.SettingsChanged += (_, _) => Interlocked.Increment(ref eventCount);

        AppSettings second = AppSettings.Default with
        {
            Indexing = AppSettings.Default.Indexing with { Roots = [@"F:\Notes"] },
        };
        await sut.SaveAsync(second, CancellationToken.None).ConfigureAwait(true);
        await Task.Delay(50).ConfigureAwait(true);

        _ = Assert.Single(Directory.GetFiles(historyDir, "settings.*.json"));
        string[] auditLines = await File.ReadAllLinesAsync(auditLog).ConfigureAwait(true);
        _ = Assert.Single(auditLines);
        Assert.Equal(0, eventCount);
    }

    [Fact]
    public async Task SaveAsync_OnIdenticalPayload_DoesNotWriteSnapshot()
    {
        string path = Path.Combine(_tempDir, "noop-snap.json");
        string historyDir = Path.Combine(_tempDir, "noop-history");
        string auditLog = Path.Combine(_tempDir, "noop-audit.log");
        _ = Directory.CreateDirectory(historyDir);

        FakeTimeProvider clock = new(new DateTimeOffset(2026, 06, 10, 12, 0, 0, TimeSpan.Zero));
        using FileSystemSettingsHistoryStore history = new(
            historyDir,
            auditLog,
            FileSystemSettingsHistoryStore.DefaultRetention,
            NullLogger<FileSystemSettingsHistoryStore>.Instance);
        using JsonSettingsService sut = new(
            path,
            NullLogger<JsonSettingsService>.Instance,
            history,
            clock);

        AppSettings target = AppSettings.Default;
        await sut.SaveAsync(target, CancellationToken.None).ConfigureAwait(true);
        await sut.SaveAsync(target, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(Directory.GetFiles(historyDir, "settings.*.json"));
        Assert.False(File.Exists(auditLog));
    }

    [Fact]
    public async Task SaveAsync_WithoutSyncContext_FallsBackToThreadPool()
    {
        string path = Path.Combine(_tempDir, "pool.json");
        using JsonSettingsService sut = new(path, NullLogger<JsonSettingsService>.Instance);
        TaskCompletionSource<int> handlerCalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.SettingsChanged += (_, _) => handlerCalled.TrySetResult(Environment.CurrentManagedThreadId);

        AppSettings target = AppSettings.Default with
        {
            Behavior = AppSettings.Default.Behavior with { SearchDebounceMs = 777 },
        };

        // Save bewusst von einem ThreadPool-Worker aus aufrufen, der keinen
        // SynchronizationContext besitzt — gleicher Pfad wie Host-Background-Service.
        await Task.Run(async () =>
        {
            Assert.Null(SynchronizationContext.Current);
            await sut.SaveAsync(target, CancellationToken.None).ConfigureAwait(false);
        }).ConfigureAwait(true);

        int handlerThreadId = await handlerCalled.Task
            .WaitAsync(TimeSpan.FromSeconds(5))
            .ConfigureAwait(true);
        Assert.True(handlerThreadId > 0);
    }

    /// <summary>
    /// Test-Implementation eines <see cref="TimeProvider"/>, der einen festen
    /// Zeitpunkt liefert und über <see cref="Advance"/> deterministisch weitergeschoben wird.
    /// </summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public FakeTimeProvider(DateTimeOffset start) => _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }

    /// <summary>
    /// Speichert alle <see cref="SynchronizationContext.Post"/>-Aufrufe in einer
    /// Queue und führt sie erst beim expliziten <see cref="DrainPending"/> aus —
    /// so kann der Test verifizieren, dass das Event nicht synchron auf dem
    /// fortsetzenden Worker, sondern über den erfassten Context zugestellt wird.
    /// </summary>
    private sealed class QueueingSynchronizationContext : SynchronizationContext
    {
        private readonly List<(SendOrPostCallback Callback, object? State)> _pending = [];
        private readonly object _gate = new();

        public int PostCount
        {
            get
            {
                lock (_gate)
                {
                    return _pending.Count;
                }
            }
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);
            lock (_gate)
            {
                _pending.Add((d, state));
            }
        }

        public void DrainPending()
        {
            (SendOrPostCallback Callback, object? State)[] snapshot;
            lock (_gate)
            {
                snapshot = [.. _pending];
                _pending.Clear();
            }
            foreach ((SendOrPostCallback callback, object? state) in snapshot)
            {
                callback(state);
            }
        }
    }
}
