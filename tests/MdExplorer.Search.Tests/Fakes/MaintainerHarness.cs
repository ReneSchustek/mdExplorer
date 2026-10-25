using System.Data.Common;
using System.Text;
using MdExplorer.Core.Abstractions;
using MdExplorer.Search.Options;
using MdExplorer.Search.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.Search.Tests.Fakes;

/// <summary>Testdaten für die Indexpflege.</summary>
internal static class MaintainerDocuments
{
    /// <summary>Frist, die nur greift, wenn etwas hängt.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    public static SearchSourceDocument Document(Guid id, string hash, string path, string frontmatterJson = "{}") =>
        new(id, "titel", path, Path.GetFileName(path), hash, frontmatterJson);
}

/// <summary>Hält Dienstanbieter, Fakes und den Maintainer für einen Testlauf zusammen.</summary>
internal sealed class LoopHarness : IAsyncDisposable
{
    // Die Voreinstellung von SearchOptions; die Uhr wird je Schritt um genau ein Intervall vorgestellt.
    private const int IntervalSeconds = 5;

    private readonly ServiceProvider _provider;
    private readonly FakeTimeProvider _time = new();
    private bool _started;

    public LoopHarness()
        : this(null)
    {
    }

    public LoopHarness(Exception? failure, int batchSize = 200)
    {
        Source = new CountingSource(failure);
        Storage = new RecordingStorage();
        Files = new ControllableFileSystem();

        ServiceCollection services = new();
        _ = services.AddScoped<ISearchSourceProvider>(_ => Source);
        _ = services.AddScoped<ISearchIndexStorage>(_ => Storage);
        _provider = services.BuildServiceProvider(validateScopes: true);

        Sut = new Fts5IndexMaintainer(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            Files,
            Microsoft.Extensions.Options.Options.Create(
                new SearchOptions { IndexMaintenanceIntervalSeconds = IntervalSeconds, MaintenanceBatchSize = batchSize }),
            _time,
            ServiceStatus,
            Logger);
    }

    public CountingSource Source { get; }

    public MdExplorer.Core.Diagnostics.BackgroundServiceStatus ServiceStatus { get; } = new();

    public RecordingStorage Storage { get; }

    public ControllableFileSystem Files { get; }

    public CountingLogger Logger { get; } = new();

    public Fts5IndexMaintainer Sut { get; }

    public void AdvanceTime(TimeSpan delta) => _time.Advance(delta);

    public async Task StartAsync()
    {
        _started = true;
        await Sut.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Wartet, bis mindestens <paramref name="count"/> Synchronisationen gelaufen sind. Der
    /// erste Lauf kommt beim Start; jeder weitere nach genau einem Intervall. Der Zeitgeber
    /// besteht schon vor dem ersten Lauf, ein Vorstellen der Uhr geht also nie verloren.
    /// </summary>
    public async Task<bool> WaitForRunsAsync(int count)
    {
        while (Source.CallCount < count)
        {
            int seen = Source.CallCount;
            if (seen > 0)
            {
                _time.Advance(TimeSpan.FromSeconds(IntervalSeconds));
            }
            if (!await Source.WaitForCallsAsync(seen + 1, MaintainerDocuments.Patience).ConfigureAwait(false))
            {
                return false;
            }
        }
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_started)
        {
            await Sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
        Sut.Dispose();
        await _provider.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>Quelle, die ihre Aufrufe zählt und wahlweise immer denselben Fehler wirft.</summary>
internal sealed class CountingSource : ISearchSourceProvider
{
    private readonly Exception? _failure;
    private readonly Lock _gate = new();
    private readonly List<(int Target, TaskCompletionSource Done)> _waiters = [];
    private int _calls;
    private int _versionCalls;

    public CountingSource(Exception? failure) => _failure = failure;

    /// <summary>Endet, sobald die Quelle <paramref name="target"/>-mal gelesen wurde, oder nach der Frist.</summary>
    public async Task<bool> WaitForCallsAsync(int target, TimeSpan timeout)
    {
        TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_calls >= target)
            {
                return true;
            }
            _waiters.Add((target, done));
        }
        try
        {
            await done.Task.WaitAsync(timeout, TestContext.Current.CancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public List<SearchSourceDocument> Documents { get; } = [];

    public Dictionary<Guid, IReadOnlyList<string>> TagsById { get; } = [];

    /// <summary>Fester Änderungsmarker; ohne ihn meldet jeder Aufruf einen neuen Stand.</summary>
    public SearchSourceVersion? FixedVersion { get; set; }

    public Task<SearchSourceVersion> LoadVersionAsync(CancellationToken cancellationToken)
    {
        int next;
        lock (_gate)
        {
            next = ++_versionCalls;
        }
        return Task.FromResult(FixedVersion ?? new SearchSourceVersion(next, null, 0, null));
    }

    public int CallCount
    {
        get
        {
            lock (_gate)
            {
                return _calls;
            }
        }
    }

    public Task<SearchSourceData> LoadAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _calls++;
            foreach ((int target, TaskCompletionSource done) in _waiters.Where(waiter => waiter.Target <= _calls).ToList())
            {
                _ = done.TrySetResult();
                _ = _waiters.Remove((target, done));
            }
        }
        return _failure is not null
            ? Task.FromException<SearchSourceData>(_failure)
            : Task.FromResult(new SearchSourceData(
                [.. Documents],
                new Dictionary<Guid, IReadOnlyList<string>>(TagsById)));
    }
}

/// <summary>Index-Speicher, der Schreibvorgänge mitschreibt statt sie auszuführen.</summary>
internal sealed class RecordingStorage : ISearchIndexStorage
{
    public Dictionary<Guid, string> Indexed { get; } = [];

    public List<SearchIndexEntry> Entries { get; } = [];

    public int ApplyCallCount { get; private set; }

    public Task<IReadOnlyDictionary<Guid, string>> LoadIndexedHashesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>(Indexed));

    public Task ApplyChangesAsync(
        IReadOnlyCollection<Guid> deletes,
        IReadOnlyCollection<SearchIndexEntry> upserts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deletes);
        ArgumentNullException.ThrowIfNull(upserts);

        ApplyCallCount++;
        foreach (Guid id in deletes)
        {
            _ = Indexed.Remove(id);
        }
        Entries.Clear();
        foreach (SearchIndexEntry entry in upserts)
        {
            Entries.Add(entry);
            Indexed[entry.MarkdownFileId] = entry.SourceContentHash;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SearchIndexHit>> QueryAsync(SearchIndexQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SearchIndexHit>>([]);

    public Task<IReadOnlyDictionary<Guid, string>> LoadBodiesAsync(IReadOnlyCollection<Guid> markdownFileIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

/// <summary>Dateisystem, dessen Inhalte und Lesefehler der Test vorgibt.</summary>
internal sealed class ControllableFileSystem : IFileSystem
{
    private readonly Dictionary<string, byte[]> _contents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> _failures = new(StringComparer.OrdinalIgnoreCase);

    // Wer Inhalt setzt, macht die Datei lesbar: Ein vorher eingetragener Fehler entfällt.
    public void SetContent(string path, string content)
    {
        _contents[path] = Encoding.UTF8.GetBytes(content);
        _ = _failures.Remove(path);
    }

    public int ReadCount { get; private set; }

    public void FailWith(string path, Exception failure) => _failures[path] = failure;

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
    {
        ReadCount++;
        return _failures.TryGetValue(path, out Exception? failure)
            ? Task.FromException<byte[]>(failure)
            : Task.FromResult(_contents.TryGetValue(path, out byte[]? content) ? content : []);
    }

    public bool DirectoryExists(string path) => false;

    public bool FileExists(string path) => _contents.ContainsKey(path);

    public void EnsureDirectoryExists(string path)
    {
    }

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive) => [];

    public IEnumerable<string> EnumerateDirectories(string directory) => [];

    public bool IsReparsePoint(string path) => false;

    public string GetDirectoryFinalPath(string path) => path;

    public byte[] ReadAllBytes(string path) => _contents.TryGetValue(path, out byte[]? content) ? content : [];

    public Stream OpenRead(string path) => Stream.Null;

    public DateTime GetLastWriteTimeUtc(string path) => DateTime.UnixEpoch;

    public long GetFileSize(string path) => 0;

    /// <summary>Wie <see cref="WriteAllBytesAtomicAsync"/>, synchron.</summary>
    public void WriteAllBytesAtomic(string path, ReadOnlySpan<byte> content) =>
        WriteAllBytesAtomicAsync(path, content.ToArray(), CancellationToken.None).GetAwaiter().GetResult();

    public Task WriteAllBytesAtomicAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public void MoveFile(string sourcePath, string destinationPath) =>
        throw new NotSupportedException("Diese Attrappe kennt keine Datei-Operationen.");

    /// <inheritdoc />
    public void DeleteFile(string path) =>
        throw new NotSupportedException("Diese Attrappe kennt keine Datei-Operationen.");
}

/// <summary>Zählt Warnungen mit angehängter Ausnahme, also Einträge mit vollem Stapel.</summary>
internal sealed class CountingLogger : ILogger<Fts5IndexMaintainer>
{
    public int WarningsWithException { get; private set; }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning && exception is not null)
        {
            WarningsWithException++;
        }
    }
}

/// <summary><see cref="DbException"/> ist abstrakt; für den Fehlerpfad braucht es eine eigene Ausprägung.</summary>
internal sealed class TestDbException : DbException
{
    public TestDbException()
    {
    }

    public TestDbException(string message)
        : base(message)
    {
    }

    public TestDbException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
