using System.Data.Common;
using System.Text;
using MdExplorer.Core.Abstractions;
using MdExplorer.Search.Options;
using MdExplorer.Search.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.Search.Tests.Services;

/// <summary>
/// Prüft den Hintergrunddienst-Anteil des <see cref="Fts5IndexMaintainer"/>: die Polling-Schleife
/// und den Weg von der Quelldatei bis zum Index-Eintrag. Die Schleife entscheidet darüber, ob der
/// Index überhaupt jemals nachgezogen wird — und ob ein einzelner Fehlschlag den Dienst beendet oder
/// nur einen Durchlauf kostet. Beides ist ohne Test nicht zu sehen, weil es keinen Rückgabewert hat.
/// Die Zeit kommt aus einem <see cref="FakeTimeProvider"/>, damit kein Test auf echte Sekunden wartet.
/// </summary>
public sealed class Fts5IndexMaintainerLoopTests
{
    private static readonly Guid IdA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid IdB = new("22222222-2222-2222-2222-222222222222");

    private static readonly TimeSpan Geduld = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ExecuteAsync_OnStart_SynchronizesBeforeTheFirstTick()
    {
        // Ohne den Vorab-Lauf bliebe der Index nach dem Programmstart eine volle
        // Intervall-Länge veraltet — sichtbar als "Suche findet die gerade erst
        // geöffnete Datei nicht".
        Schleifenumgebung u = new();
        await using (u.ConfigureAwait(true))
        {
            await u.StartAsync().ConfigureAwait(true);

            Assert.True(
                await u.WarteAufLaeufeAsync(1).ConfigureAwait(true),
                $"Erwartet: 1 Lauf, tatsächlich {u.Quelle.CallCount}.");
        }
    }

    [Fact]
    public async Task ExecuteAsync_OnTimerTick_SynchronizesAgain()
    {
        Schleifenumgebung u = new();
        await using (u.ConfigureAwait(true))
        {
            await u.StartAsync().ConfigureAwait(true);
            _ = await u.WarteAufLaeufeAsync(1).ConfigureAwait(true);

            Assert.True(
                await u.WarteAufLaeufeAsync(2).ConfigureAwait(true),
                $"Erwartet: 2 Läufe, tatsächlich {u.Quelle.CallCount}.");
        }
    }

    [Fact]
    public async Task ExecuteAsync_OnRepeatedTicks_KeepsSynchronizing()
    {
        Schleifenumgebung u = new();
        await using (u.ConfigureAwait(true))
        {
            await u.StartAsync().ConfigureAwait(true);

            Assert.True(
                await u.WarteAufLaeufeAsync(4).ConfigureAwait(true),
                $"Erwartet: 4 Läufe, tatsächlich {u.Quelle.CallCount}.");
        }
    }

    [Fact]
    public async Task ExecuteAsync_OnStop_EndsWithoutFault()
    {
        Schleifenumgebung u = new();
        await using (u.ConfigureAwait(true))
        {
            await u.StartAsync().ConfigureAwait(true);
            _ = await u.WarteAufLaeufeAsync(1).ConfigureAwait(true);

            await u.Sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            // Ein Abbruch ist der Normalfall beim Herunterfahren und darf keinen Fehler hinterlassen.
            Assert.NotNull(u.Sut.ExecuteTask);
            Assert.True(u.Sut.ExecuteTask!.IsCompleted);
            Assert.False(u.Sut.ExecuteTask.IsFaulted);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenSynchronizationFailsWithDbException_KeepsPolling()
    {
        // Eine Datenbank-Spitze darf den Dienst nicht beenden, sonst bleibt der Index bis zum
        // nächsten Programmstart stehen — und niemand bemerkt es.
        Schleifenumgebung u = new(new TestDbException("Datenbank belegt"));
        await using (u.ConfigureAwait(true))
        {
            await u.StartAsync().ConfigureAwait(true);

            Assert.True(
                await u.WarteAufLaeufeAsync(3).ConfigureAwait(true),
                $"Erwartet: 3 Läufe trotz Fehler, tatsächlich {u.Quelle.CallCount}.");
            Assert.False(u.Sut.ExecuteTask!.IsFaulted);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenSynchronizationFailsWithArgumentException_KeepsPolling()
    {
        Schleifenumgebung u = new(new ArgumentException("kaputter Zustand"));
        await using (u.ConfigureAwait(true))
        {
            await u.StartAsync().ConfigureAwait(true);

            Assert.True(
                await u.WarteAufLaeufeAsync(3).ConfigureAwait(true),
                $"Erwartet: 3 Läufe trotz Fehler, tatsächlich {u.Quelle.CallCount}.");
            Assert.False(u.Sut.ExecuteTask!.IsFaulted);
        }
    }

    [Fact]
    public async Task TrySynchronizeAsync_OnDbException_DoesNotThrow()
    {
        Schleifenumgebung u = new(new TestDbException("Datenbank belegt"));
        await using (u.ConfigureAwait(true))
        {
            await u.Sut.TrySynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(1, u.Quelle.CallCount);
        }
    }

    [Fact]
    public async Task TrySynchronizeAsync_WhenCancelled_Rethrows()
    {
        // Der Abbruch muss durchschlagen, damit die Schleife anhält statt weiterzupollen.
        Schleifenumgebung u = new(new OperationCanceledException());
        await using (u.ConfigureAwait(true))
        {
            using CancellationTokenSource abbruch = new();
            await abbruch.CancelAsync().ConfigureAwait(true);

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => u.Sut.TrySynchronizeAsync(abbruch.Token)).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_OnUnchangedIndex_ReportsNoChangesAndSkipsWriting()
    {
        Schleifenumgebung u = new();
        await using (u.ConfigureAwait(true))
        {
            u.Quelle.Dokumente.Add(Dokument(IdA, "hash-A", @"C:\notizen\a.md"));
            u.Dateien.Setze(@"C:\notizen\a.md", "Inhalt A");
            _ = await u.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            int schreibvorgaenge = u.Speicher.ApplyCallCount;

            int changed = await u.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(0, changed);
            Assert.Equal(schreibvorgaenge, u.Speicher.ApplyCallCount);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_OnNewDocument_WritesBodyWithoutFrontmatter()
    {
        Schleifenumgebung u = new();
        await using (u.ConfigureAwait(true))
        {
            u.Quelle.Dokumente.Add(Dokument(IdA, "hash-A", @"C:\notizen\a.md"));
            u.Dateien.Setze(@"C:\notizen\a.md", "---\ntitle: Bericht\n---\nDer eigentliche Text.");

            int changed = await u.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(1, changed);
            SearchIndexEntry eintrag = Assert.Single(u.Speicher.Entries);
            // Der Frontmatter-Block gehört in die eigene Spalte, nicht in den Fließtext —
            // sonst treffen Suchen nach "title" jedes Dokument.
            Assert.Equal("Der eigentliche Text.", eintrag.Body, StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_WithTags_JoinsThemIntoOneColumn()
    {
        Schleifenumgebung u = new();
        await using (u.ConfigureAwait(true))
        {
            u.Quelle.Dokumente.Add(Dokument(IdA, "hash-A", @"C:\notizen\a.md"));
            u.Quelle.TagsNachId[IdA] = ["bericht", "quartal"];
            u.Dateien.Setze(@"C:\notizen\a.md", "Text");

            _ = await u.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            SearchIndexEntry eintrag = Assert.Single(u.Speicher.Entries);
            Assert.Equal("bericht quartal", eintrag.Tags, StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_WithoutTags_LeavesTheTagColumnEmpty()
    {
        Schleifenumgebung u = new();
        await using (u.ConfigureAwait(true))
        {
            u.Quelle.Dokumente.Add(Dokument(IdA, "hash-A", @"C:\notizen\a.md"));
            u.Dateien.Setze(@"C:\notizen\a.md", "Text");

            _ = await u.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(string.Empty, Assert.Single(u.Speicher.Entries).Tags, StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_WithEmptyTagList_LeavesTheTagColumnEmpty()
    {
        Schleifenumgebung u = new();
        await using (u.ConfigureAwait(true))
        {
            u.Quelle.Dokumente.Add(Dokument(IdA, "hash-A", @"C:\notizen\a.md"));
            u.Quelle.TagsNachId[IdA] = [];
            u.Dateien.Setze(@"C:\notizen\a.md", "Text");

            _ = await u.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(string.Empty, Assert.Single(u.Speicher.Entries).Tags, StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_WithFrontmatterJson_FlattensItIntoItsOwnColumn()
    {
        Schleifenumgebung u = new();
        await using (u.ConfigureAwait(true))
        {
            u.Quelle.Dokumente.Add(Dokument(IdA, "hash-A", @"C:\notizen\a.md", """{"title":"Bericht"}"""));
            u.Dateien.Setze(@"C:\notizen\a.md", "Text");

            _ = await u.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            SearchIndexEntry eintrag = Assert.Single(u.Speicher.Entries);
            Assert.Contains("Bericht", eintrag.Frontmatter, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_WhenTheFileCannotBeRead_IndexesTheDocumentWithAnEmptyBody()
    {
        // Eine gesperrte Datei darf den ganzen Durchlauf nicht abbrechen: Titel, Tags und
        // Pfad bleiben durchsuchbar, nur der Fließtext fehlt.
        Schleifenumgebung u = new();
        await using (u.ConfigureAwait(true))
        {
            u.Quelle.Dokumente.Add(Dokument(IdA, "hash-A", @"C:\notizen\gesperrt.md"));
            u.Dateien.LasseScheitern(@"C:\notizen\gesperrt.md", new IOException("Datei gesperrt"));

            int changed = await u.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(1, changed);
            SearchIndexEntry eintrag = Assert.Single(u.Speicher.Entries);
            Assert.Equal(string.Empty, eintrag.Body, StringComparer.Ordinal);
            Assert.Equal("titel", eintrag.Title, StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_WhenTheFileIsMissing_IndexesTheDocumentWithAnEmptyBody()
    {
        Schleifenumgebung u = new();
        await using (u.ConfigureAwait(true))
        {
            u.Quelle.Dokumente.Add(Dokument(IdA, "hash-A", @"C:\notizen\fehlt.md"));
            u.Dateien.LasseScheitern(@"C:\notizen\fehlt.md", new FileNotFoundException("weg"));

            _ = await u.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(string.Empty, Assert.Single(u.Speicher.Entries).Body, StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_WhenADocumentDisappears_RemovesItFromTheIndex()
    {
        Schleifenumgebung u = new();
        await using (u.ConfigureAwait(true))
        {
            u.Quelle.Dokumente.Add(Dokument(IdA, "hash-A", @"C:\notizen\a.md"));
            u.Quelle.Dokumente.Add(Dokument(IdB, "hash-B", @"C:\notizen\b.md"));
            u.Dateien.Setze(@"C:\notizen\a.md", "A");
            u.Dateien.Setze(@"C:\notizen\b.md", "B");
            _ = await u.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            _ = u.Quelle.Dokumente.RemoveAll(d => d.MarkdownFileId == IdB);
            int changed = await u.Sut.SynchronizeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.Equal(1, changed);
            Assert.Contains(IdA, u.Speicher.Indiziert.Keys);
            Assert.DoesNotContain(IdB, u.Speicher.Indiziert.Keys);
        }
    }

    private static SearchSourceDocument Dokument(Guid id, string hash, string pfad, string frontmatterJson = "{}") =>
        new(id, "titel", pfad, Path.GetFileName(pfad), hash, frontmatterJson);

    /// <summary>Hält Dienstanbieter, Fakes und den Maintainer für einen Testlauf zusammen.</summary>
    private sealed class Schleifenumgebung : IAsyncDisposable
    {
        private const int IntervallSekunden = 5;

        private readonly ServiceProvider _provider;
        private readonly FakeTimeProvider _zeit = new();
        private bool _gestartet;

        public Schleifenumgebung()
            : this(null)
        {
        }

        public Schleifenumgebung(Exception? fehler)
        {
            Quelle = new CountingSource(fehler);
            Speicher = new RecordingStorage();
            Dateien = new SteuerbaresDateisystem();

            ServiceCollection dienste = new();
            _ = dienste.AddScoped<ISearchSourceProvider>(_ => Quelle);
            _ = dienste.AddScoped<ISearchIndexStorage>(_ => Speicher);
            _provider = dienste.BuildServiceProvider(validateScopes: true);

            Sut = new Fts5IndexMaintainer(
                _provider.GetRequiredService<IServiceScopeFactory>(),
                Dateien,
                Microsoft.Extensions.Options.Options.Create(
                    new SearchOptions { IndexMaintenanceIntervalSeconds = IntervallSekunden }),
                _zeit,
                NullLogger<Fts5IndexMaintainer>.Instance);
        }

        public CountingSource Quelle { get; }

        public RecordingStorage Speicher { get; }

        public SteuerbaresDateisystem Dateien { get; }

        public Fts5IndexMaintainer Sut { get; }

        public async Task StartAsync()
        {
            _gestartet = true;
            await Sut.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Wartet, bis mindestens <paramref name="anzahl"/> Synchronisationen gelaufen sind, und
        /// treibt dabei die Uhr weiter. Das Vorstellen geschieht in der Schleife statt einmalig,
        /// weil sonst ein Tick verloren gehen kann, solange die Schleife den Zeitgeber noch nicht
        /// erreicht hat — der Test würde dann je nach Maschinenlast unterschiedlich ausgehen.
        /// </summary>
        public async Task<bool> WarteAufLaeufeAsync(int anzahl)
        {
            DateTimeOffset ende = DateTimeOffset.UtcNow + Geduld;
            while (DateTimeOffset.UtcNow < ende)
            {
                if (Quelle.CallCount >= anzahl)
                {
                    return true;
                }
                _zeit.Advance(TimeSpan.FromSeconds(IntervallSekunden));
                await Task.Delay(5).ConfigureAwait(false);
            }
            return Quelle.CallCount >= anzahl;
        }

        public async ValueTask DisposeAsync()
        {
            if (_gestartet)
            {
                await Sut.StopAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
            }
            Sut.Dispose();
            await _provider.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Quelle, die ihre Aufrufe zählt und wahlweise immer denselben Fehler wirft.</summary>
    private sealed class CountingSource : ISearchSourceProvider
    {
        private readonly Exception? _fehler;
        private int _aufrufe;

        public CountingSource(Exception? fehler) => _fehler = fehler;

        public List<SearchSourceDocument> Dokumente { get; } = [];

        public Dictionary<Guid, IReadOnlyList<string>> TagsNachId { get; } = [];

        public int CallCount => Volatile.Read(ref _aufrufe);

        public Task<SearchSourceData> LoadAsync(CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref _aufrufe);
            return _fehler is not null
                ? Task.FromException<SearchSourceData>(_fehler)
                : Task.FromResult(new SearchSourceData(
                    [.. Dokumente],
                    new Dictionary<Guid, IReadOnlyList<string>>(TagsNachId)));
        }
    }

    /// <summary>Index-Speicher, der Schreibvorgänge mitschreibt statt sie auszuführen.</summary>
    private sealed class RecordingStorage : ISearchIndexStorage
    {
        public Dictionary<Guid, string> Indiziert { get; } = [];

        public List<SearchIndexEntry> Entries { get; } = [];

        public int ApplyCallCount { get; private set; }

        public Task<IReadOnlyDictionary<Guid, string>> LoadIndexedHashesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>(Indiziert));

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
                _ = Indiziert.Remove(id);
            }
            Entries.Clear();
            foreach (SearchIndexEntry eintrag in upserts)
            {
                Entries.Add(eintrag);
                Indiziert[eintrag.MarkdownFileId] = eintrag.SourceContentHash;
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SearchIndexHit>> QueryAsync(SearchIndexQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SearchIndexHit>>([]);

        public Task<IReadOnlyDictionary<Guid, string>> LoadBodiesAsync(IReadOnlyCollection<Guid> markdownFileIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    /// <summary>Dateisystem, dessen Inhalte und Lesefehler der Test vorgibt.</summary>
    private sealed class SteuerbaresDateisystem : IFileSystem
    {
        private readonly Dictionary<string, byte[]> _inhalte = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Exception> _fehler = new(StringComparer.OrdinalIgnoreCase);

        public void Setze(string pfad, string inhalt) => _inhalte[pfad] = Encoding.UTF8.GetBytes(inhalt);

        public void LasseScheitern(string pfad, Exception fehler) => _fehler[pfad] = fehler;

        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
            _fehler.TryGetValue(path, out Exception? fehler)
                ? Task.FromException<byte[]>(fehler)
                : Task.FromResult(_inhalte.TryGetValue(path, out byte[]? inhalt) ? inhalt : []);

        public bool DirectoryExists(string path) => false;

        public bool FileExists(string path) => _inhalte.ContainsKey(path);

        public void EnsureDirectoryExists(string path)
        {
        }

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive) => [];

        public IEnumerable<string> EnumerateDirectories(string directory) => [];

        public bool IsReparsePoint(string path) => false;

        public string GetDirectoryFinalPath(string path) => path;

        public byte[] ReadAllBytes(string path) => _inhalte.TryGetValue(path, out byte[]? inhalt) ? inhalt : [];

        public Stream OpenRead(string path) => Stream.Null;

        public DateTime GetLastWriteTimeUtc(string path) => DateTime.UnixEpoch;

        public long GetFileSize(string path) => 0;

        public Task WriteAllBytesAtomicAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) => Task.CompletedTask;

        /// <inheritdoc />
        public void MoveFile(string sourcePath, string destinationPath) =>
            throw new NotSupportedException("Diese Attrappe kennt keine Datei-Operationen.");

        /// <inheritdoc />
        public void DeleteFile(string path) =>
            throw new NotSupportedException("Diese Attrappe kennt keine Datei-Operationen.");
    }

    /// <summary><see cref="DbException"/> ist abstrakt — für den Fehlerpfad braucht es eine eigene Ausprägung.</summary>
    private sealed class TestDbException : DbException
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
}
