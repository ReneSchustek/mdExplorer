using System.Diagnostics.CodeAnalysis;
using MdExplorer.Core.Abstractions;
using MdExplorer.Search.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.Search.Tests.Services;

/// <summary>
/// Die Helfer des <see cref="Fts5IndexMaintainer"/> einzeln: Der Abgleich nimmt neue und geänderte
/// Dokumente portionsweise auf, erklärt nur wirklich verschwundene zu Waisen und kommt über mehrere
/// Takte zum vollständigen Index. Hielte er ein lebendes Dokument jenseits der Portionsgrenze für
/// verwaist, löschte jeder Takt, was der vorige gerade eingetragen hat.
/// </summary>
public sealed class Fts5IndexMaintainerTests
{
    private static readonly Guid IdA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid IdB = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid IdC = new("33333333-3333-3333-3333-333333333333");

    // Kein Dokument wartet auf einen neuen Leseversuch; der Abgleich entscheidet allein nach Hash.
    private static readonly Func<SearchSourceDocument, bool> NothingWaits = static _ => false;

    [Fact]
    public void ComputeDiff_OnEmptyIndexAndEmptySource_ReturnsEmptyDiff()
    {
        Dictionary<Guid, string> indexedHashes = [];
        SearchSourceData source = new([], new Dictionary<Guid, IReadOnlyList<string>>());

        SynchronizationDiff diff = Fts5IndexMaintainer.ComputeDiffCore(indexedHashes, source, batchSize: 100, NothingWaits);

        Assert.Empty(diff.Targets);
        Assert.Empty(diff.Orphans);
    }

    [Fact]
    public void ComputeDiff_OnNewDocumentNotInIndex_AddsToTargets()
    {
        Dictionary<Guid, string> indexedHashes = [];
        SearchSourceData source = new(
            [NewDoc(IdA, "hash-A")],
            new Dictionary<Guid, IReadOnlyList<string>>());

        SynchronizationDiff diff = Fts5IndexMaintainer.ComputeDiffCore(indexedHashes, source, batchSize: 100, NothingWaits);

        SearchSourceDocument single = Assert.Single(diff.Targets);
        Assert.Equal(IdA, single.MarkdownFileId);
        Assert.Empty(diff.Orphans);
    }

    [Fact]
    public void ComputeDiff_OnHashUnchanged_DoesNotAddToTargets()
    {
        Dictionary<Guid, string> indexedHashes = new() { [IdA] = "hash-A" };
        SearchSourceData source = new(
            [NewDoc(IdA, "hash-A")],
            new Dictionary<Guid, IReadOnlyList<string>>());

        SynchronizationDiff diff = Fts5IndexMaintainer.ComputeDiffCore(indexedHashes, source, batchSize: 100, NothingWaits);

        Assert.Empty(diff.Targets);
        Assert.Empty(diff.Orphans);
    }

    [Fact]
    public void ComputeDiff_OnHashChanged_AddsToTargets()
    {
        Dictionary<Guid, string> indexedHashes = new() { [IdA] = "hash-A-old" };
        SearchSourceData source = new(
            [NewDoc(IdA, "hash-A-new")],
            new Dictionary<Guid, IReadOnlyList<string>>());

        SynchronizationDiff diff = Fts5IndexMaintainer.ComputeDiffCore(indexedHashes, source, batchSize: 100, NothingWaits);

        SearchSourceDocument single = Assert.Single(diff.Targets);
        Assert.Equal(IdA, single.MarkdownFileId);
    }

    [Fact]
    public void ComputeDiff_OnIndexedDocNotInSource_AddsToOrphans()
    {
        Dictionary<Guid, string> indexedHashes = new() { [IdA] = "hash-A", [IdB] = "hash-B" };
        SearchSourceData source = new(
            [NewDoc(IdA, "hash-A")],
            new Dictionary<Guid, IReadOnlyList<string>>());

        SynchronizationDiff diff = Fts5IndexMaintainer.ComputeDiffCore(indexedHashes, source, batchSize: 100, NothingWaits);

        Assert.Empty(diff.Targets);
        Guid orphan = Assert.Single(diff.Orphans);
        Assert.Equal(IdB, orphan);
    }

    [Fact]
    public void ComputeDiff_AtMaintenanceBatchSize_StopsAddingTargets()
    {
        Dictionary<Guid, string> indexedHashes = [];
        SearchSourceData source = new(
            [NewDoc(IdA, "hash-A"), NewDoc(IdB, "hash-B"), NewDoc(IdC, "hash-C")],
            new Dictionary<Guid, IReadOnlyList<string>>());

        SynchronizationDiff diff = Fts5IndexMaintainer.ComputeDiffCore(indexedHashes, source, batchSize: 2, NothingWaits);

        Assert.Equal(2, diff.Targets.Count);
    }

    [Fact]
    public void ComputeDiff_OnADocumentWaitingForRetry_LeavesItsBatchSlotToTheNext()
    {
        Dictionary<Guid, string> indexedHashes = new() { [IdA] = Fts5IndexMaintainer.UnreadBodyHash };
        SearchSourceData source = new(
            [NewDoc(IdA, "hash-A"), NewDoc(IdB, "hash-B")],
            new Dictionary<Guid, IReadOnlyList<string>>());

        SynchronizationDiff diff = Fts5IndexMaintainer.ComputeDiffCore(
            indexedHashes, source, batchSize: 1, document => document.MarkdownFileId == IdA);

        Assert.Equal(IdB, Assert.Single(diff.Targets).MarkdownFileId);
        Assert.Empty(diff.Orphans);
    }

    [Fact]
    public void StripFrontmatter_OnNoFrontmatter_ReturnsSourceUnchanged()
    {
        const string Source = "# Heading\n\nBody.";

        string result = SearchIndexText.StripFrontmatter(Source);

        Assert.Equal(Source, result);
    }

    [Fact]
    public void StripFrontmatter_OnMissingClosingMarker_ReturnsSourceUnchanged()
    {
        const string Source = "---\ntitle: Test\n# Heading\n";

        string result = SearchIndexText.StripFrontmatter(Source);

        Assert.Equal(Source, result);
    }

    [Fact]
    public void StripFrontmatter_OnValidFrontmatter_LF_ReturnsBodyAfter()
    {
        const string Source = "---\ntitle: Test\n---\nBody-Inhalt.";

        string result = SearchIndexText.StripFrontmatter(Source);

        Assert.Equal("Body-Inhalt.", result);
    }

    [Fact]
    public void StripFrontmatter_OnFrontmatterButNoBody_ReturnsEmpty()
    {
        const string Source = "---\ntitle: Test\n---";

        string result = SearchIndexText.StripFrontmatter(Source);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void FlattenFrontmatter_OnInvalidJson_ReturnsEmpty()
    {
        string result = SearchIndexText.FlattenFrontmatter("{ kaputt :: ");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void FlattenFrontmatter_OnNullOrWhitespace_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SearchIndexText.FlattenFrontmatter(string.Empty));
        Assert.Equal(string.Empty, SearchIndexText.FlattenFrontmatter("   "));
    }

    [Fact]
    public void FlattenFrontmatter_OnSimpleObject_ReturnsKeyValuePairs()
    {
        string result = SearchIndexText.FlattenFrontmatter("""{"title":"Foo","author":"Bar"}""");

        Assert.Contains("title", result, StringComparison.Ordinal);
        Assert.Contains("Foo", result, StringComparison.Ordinal);
        Assert.Contains("author", result, StringComparison.Ordinal);
        Assert.Contains("Bar", result, StringComparison.Ordinal);
    }

    [Fact]
    public void FlattenFrontmatter_OnArrayValue_ReturnsJsonRepresentation()
    {
        string result = SearchIndexText.FlattenFrontmatter("""{"tags":["a","b"]}""");

        // Ein Array landet in seiner JSON-Schreibweise im Text, nicht als einzelne Werte.
        Assert.Contains("tags", result, StringComparison.Ordinal);
        Assert.Contains("a", result, StringComparison.Ordinal);
        Assert.Contains("b", result, StringComparison.Ordinal);
    }

    [Fact]
    public void FlattenFrontmatter_OnNonObjectRoot_ReturnsEmpty()
    {
        string result = SearchIndexText.FlattenFrontmatter("[1, 2, 3]");

        Assert.Equal(string.Empty, result);
    }

    // TrySynchronizeAsync fängt erholbare Ausnahmen ab, damit der Abgleichtakt weiterläuft.
    [Fact]
    public async Task TrySynchronizeAsync_OnArgumentException_KeepsTheLoopAlive()
    {
        using ThrowingMaintainerHarness harness = new(new ArgumentException("simulated invariant"));

        // Darf nicht werfen.
        await harness.Sut.TrySynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(1, harness.Source.CallCount);
    }

    [Fact]
    public async Task TrySynchronizeAsync_OnInvalidOperationException_KeepsTheLoopAlive()
    {
        using ThrowingMaintainerHarness harness = new(new InvalidOperationException("simulated bad state"));

        await harness.Sut.TrySynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(1, harness.Source.CallCount);
    }

    // Die Portionsgrenze begrenzt nur, was eingetragen wird, nicht die Menge der lebenden Ids.
    [Fact]
    public void ComputeDiff_OnMoreDocumentsThanBatchSize_DoesNotMarkLiveAsOrphan()
    {
        // Fünf volle Portionen; jedes fünfte Dokument ist schon aktuell im Index.
        const int DocumentCount = 500;
        const int BatchSize = 100;
        List<SearchSourceDocument> documents = BuildDocuments(DocumentCount);
        SearchSourceData source = new(documents, new Dictionary<Guid, IReadOnlyList<string>>());
        Dictionary<Guid, string> indexedHashes = new(BatchSize);
        for (int i = 0; i < DocumentCount; i += 5)
        {
            indexedHashes[documents[i].MarkdownFileId] = documents[i].SourceContentHash;
        }

        SynchronizationDiff diff = Fts5IndexMaintainer.ComputeDiffCore(indexedHashes, source, BatchSize, NothingWaits);

        Assert.Equal(BatchSize, diff.Targets.Count);
        Assert.Empty(diff.Orphans);
    }

    [Fact]
    public async Task Synchronize_OnMoreDocumentsThanBatchSize_ConvergesOverMultipleTicks()
    {
        // Fünf Portionen zu 100 brauchen genau fünf Takte; ein sechster darf nichts mehr ändern.
        const int DocumentCount = 500;
        const int BatchSize = 100;
        List<SearchSourceDocument> documents = BuildDocuments(DocumentCount);
        SearchSourceData source = new(documents, new Dictionary<Guid, IReadOnlyList<string>>());

        using ConvergenceMaintainerHarness harness = new(source, BatchSize);

        int expectedTicks = (int)Math.Ceiling((double)DocumentCount / BatchSize);
        for (int tick = 0; tick < expectedTicks; tick++)
        {
            _ = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        }

        Assert.Equal(DocumentCount, harness.Storage.Indexed.Count);

        int idempotentChange = await harness.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(0, idempotentChange);
        Assert.Equal(DocumentCount, harness.Storage.Indexed.Count);
    }

    private static List<SearchSourceDocument> BuildDocuments(int count)
    {
        List<SearchSourceDocument> documents = new(count);
        for (int i = 0; i < count; i++)
        {
            Guid id = MakeDeterministicGuid(i);
            documents.Add(new SearchSourceDocument(
                id,
                $"title-{i}",
                $@"C:\path\{i}.md",
                $"{i}.md",
                $"hash-{i}",
                "{}"));
        }
        return documents;
    }

    private static Guid MakeDeterministicGuid(int seed)
    {
        byte[] bytes = new byte[16];
        _ = BitConverter.TryWriteBytes(bytes, seed);
        return new Guid(bytes);
    }

    private static SearchSourceDocument NewDoc(Guid id, string hash) =>
        new(id, "title", @"C:\path\file.md", "file.md", hash, "{}");

    /// <summary>Echter Maintainer über einer Quelle, die beim Laden wirft.</summary>
    private sealed class ThrowingMaintainerHarness : IDisposable
    {
        public ThrowingSearchSourceProvider Source { get; }
        public Fts5IndexMaintainer Sut { get; }
        private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;

        public ThrowingMaintainerHarness(Exception failure)
        {
            Source = new ThrowingSearchSourceProvider(failure);
            ServiceCollection services = new();
            _ = services.AddScoped<MdExplorer.Core.Abstractions.ISearchSourceProvider>(_ => Source);
            _ = services.AddScoped<MdExplorer.Core.Abstractions.ISearchIndexStorage>(_ => new NoopSearchIndexStorage());
            _provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
            MdExplorer.Search.Options.SearchOptions options = new();
            Microsoft.Extensions.Options.IOptions<MdExplorer.Search.Options.SearchOptions> wrappedOptions =
                Microsoft.Extensions.Options.Options.Create(options);
            Microsoft.Extensions.Time.Testing.FakeTimeProvider timeProvider = new();
            NoopFileSystem fs = new();

            Sut = new Fts5IndexMaintainer(
                scopeFactory,
                fs,
                wrappedOptions,
                timeProvider,
                new MdExplorer.Core.Diagnostics.BackgroundServiceStatus(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Fts5IndexMaintainer>.Instance);
        }

        public void Dispose() => _provider.Dispose();
    }

    /// <summary>Quelle, die jeden Ladeversuch mit derselben Ausnahme scheitern lässt und mitzählt.</summary>
    private sealed class ThrowingSearchSourceProvider : MdExplorer.Core.Abstractions.ISearchSourceProvider
    {
        private int _versionCalls;

        // Jeder Aufruf meldet einen neuen Stand; der volle Abgleich läuft damit jedes Mal.
        public Task<MdExplorer.Core.Abstractions.SearchSourceVersion> LoadVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MdExplorer.Core.Abstractions.SearchSourceVersion(Interlocked.Increment(ref _versionCalls), null, 0, null));

        private readonly Exception _failure;

        public int CallCount { get; private set; }

        public ThrowingSearchSourceProvider(Exception failure) => _failure = failure;

        public Task<SearchSourceData> LoadAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromException<SearchSourceData>(_failure);
        }
    }

    /// <summary>Leerer Suchindex, der jede Änderung annimmt und verwirft.</summary>
    private sealed class NoopSearchIndexStorage : MdExplorer.Core.Abstractions.ISearchIndexStorage
    {
        public Task<IReadOnlyDictionary<Guid, string>> LoadIndexedHashesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task ApplyChangesAsync(IReadOnlyCollection<Guid> deletes, IReadOnlyCollection<MdExplorer.Core.Abstractions.SearchIndexEntry> upserts, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<MdExplorer.Core.Abstractions.SearchIndexHit>> QueryAsync(MdExplorer.Core.Abstractions.SearchIndexQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MdExplorer.Core.Abstractions.SearchIndexHit>>([]);

        public Task<IReadOnlyDictionary<Guid, string>> LoadBodiesAsync(IReadOnlyCollection<Guid> markdownFileIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    /// <summary>Dateisystem ohne Dateien; jede gelesene Datei ist leer.</summary>
    private sealed class NoopFileSystem : MdExplorer.Core.Abstractions.IFileSystem
    {
        public bool DirectoryExists(string path) => false;
        public bool FileExists(string path) => false;
        public void EnsureDirectoryExists(string path) { }
        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive) => [];
        public IEnumerable<string> EnumerateDirectories(string directory) => [];
        public bool IsReparsePoint(string path) => false;
        public string GetDirectoryFinalPath(string path) => path;
        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) => Task.FromResult(Array.Empty<byte>());
        public byte[] ReadAllBytes(string path) => [];
        public System.IO.Stream OpenRead(string path) => System.IO.Stream.Null;
        public DateTime GetLastWriteTimeUtc(string path) => DateTime.UnixEpoch;
        public long GetFileSize(string path) => 0;
        public Task WriteAllBytesAtomicAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) => Task.CompletedTask;

        public void WriteAllBytesAtomic(string path, ReadOnlySpan<byte> content)
        {
        }

        /// <inheritdoc />
        public void MoveFile(string sourcePath, string destinationPath) =>
            throw new NotSupportedException("Diese Attrappe kennt keine Datei-Operationen.");

        /// <inheritdoc />
        public void DeleteFile(string path) =>
            throw new NotSupportedException("Diese Attrappe kennt keine Datei-Operationen.");
    }

    /// <summary>Echter Maintainer mit fester Portionsgröße über Quelle und Index im Arbeitsspeicher.</summary>
    private sealed class ConvergenceMaintainerHarness : IDisposable
    {
        public InMemorySearchIndexStorage Storage { get; }
        public Fts5IndexMaintainer Sut { get; }
        private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;

        public ConvergenceMaintainerHarness(SearchSourceData source, int batchSize)
        {
            Storage = new InMemorySearchIndexStorage();
            ServiceCollection services = new();
            _ = services.AddScoped<MdExplorer.Core.Abstractions.ISearchSourceProvider>(_ => new InMemorySearchSourceProvider(source));
            _ = services.AddScoped<MdExplorer.Core.Abstractions.ISearchIndexStorage>(_ => Storage);
            _provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
            MdExplorer.Search.Options.SearchOptions options = new() { MaintenanceBatchSize = batchSize };
            Microsoft.Extensions.Options.IOptions<MdExplorer.Search.Options.SearchOptions> wrappedOptions =
                Microsoft.Extensions.Options.Options.Create(options);
            Microsoft.Extensions.Time.Testing.FakeTimeProvider timeProvider = new();
            NoopFileSystem fs = new();

            Sut = new Fts5IndexMaintainer(
                scopeFactory,
                fs,
                wrappedOptions,
                timeProvider,
                new MdExplorer.Core.Diagnostics.BackgroundServiceStatus(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Fts5IndexMaintainer>.Instance);
        }

        public void Dispose() => _provider.Dispose();
    }

    /// <summary>Quelle, die immer denselben Datenstand liefert.</summary>
    private sealed class InMemorySearchSourceProvider : MdExplorer.Core.Abstractions.ISearchSourceProvider
    {
        private int _versionCalls;

        // Jeder Aufruf meldet einen neuen Stand; der volle Abgleich läuft damit jedes Mal.
        public Task<MdExplorer.Core.Abstractions.SearchSourceVersion> LoadVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MdExplorer.Core.Abstractions.SearchSourceVersion(Interlocked.Increment(ref _versionCalls), null, 0, null));

        private readonly SearchSourceData _data;

        public InMemorySearchSourceProvider(SearchSourceData data) => _data = data;

        public Task<SearchSourceData> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(_data);
    }

    /// <summary>Suchindex im Arbeitsspeicher, der nur Id und Hash je Dokument festhält.</summary>
    private sealed class InMemorySearchIndexStorage : MdExplorer.Core.Abstractions.ISearchIndexStorage
    {
        public Dictionary<Guid, string> Indexed { get; } = [];

        public Task<IReadOnlyDictionary<Guid, string>> LoadIndexedHashesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>(Indexed));

        public Task ApplyChangesAsync(
            IReadOnlyCollection<Guid> deletes,
            IReadOnlyCollection<MdExplorer.Core.Abstractions.SearchIndexEntry> upserts,
            CancellationToken cancellationToken)
        {
            foreach (Guid id in deletes)
            {
                _ = Indexed.Remove(id);
            }
            foreach (MdExplorer.Core.Abstractions.SearchIndexEntry entry in upserts)
            {
                Indexed[entry.MarkdownFileId] = entry.SourceContentHash;
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MdExplorer.Core.Abstractions.SearchIndexHit>> QueryAsync(MdExplorer.Core.Abstractions.SearchIndexQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MdExplorer.Core.Abstractions.SearchIndexHit>>([]);

        public Task<IReadOnlyDictionary<Guid, string>> LoadBodiesAsync(IReadOnlyCollection<Guid> markdownFileIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }
}
