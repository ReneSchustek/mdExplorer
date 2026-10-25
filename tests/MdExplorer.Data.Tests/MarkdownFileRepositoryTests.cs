using MdExplorer.Core.Models;
using MdExplorer.Data.Repositories;
using MdExplorer.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Tests;

/// <summary>
/// Prüft das Datei-Repository gegen eine SQLite-Datenbank im Arbeitsspeicher.
/// Schwerpunkt sind die Pfade, die im Betrieb selten auftreten und deshalb leicht
/// unbemerkt brechen: Entfernen nicht verfolgter Entitäten, Namenssuche ohne Treffer
/// und die Abgrenzung gleichnamiger Wurzelverzeichnisse.
/// </summary>
public sealed class MarkdownFileRepositoryTests : IAsyncDisposable
{
    private readonly TestIdSequence _ids = new();
    private readonly SqliteConnection _connection;
    private readonly MdExplorerDbContext _dbContext;
    private readonly MarkdownFileRepository _sut;

    public MarkdownFileRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<MdExplorerDbContext> options = new DbContextOptionsBuilder<MdExplorerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

        _dbContext = new MdExplorerDbContext(options);
        _ = _dbContext.Database.EnsureCreated();
        _sut = new MarkdownFileRepository(_dbContext);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public void Constructor_WithoutContext_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new MarkdownFileRepository(null!));

    [Fact]
    public async Task GetByIdAsync_WithEmptyGuid_ReturnsNullWithoutQuerying()
    {
        MarkdownFile? hit = await _sut.GetByIdAsync(Guid.Empty, TestContext.Current.CancellationToken);

        Assert.Null(hit);
    }

    [Fact]
    public async Task GetByIdAsync_WithUnknownId_ReturnsNull()
    {
        MarkdownFile? hit = await _sut.GetByIdAsync(Guid.Parse("99999999-9999-9999-9999-999999999999"), TestContext.Current.CancellationToken);

        Assert.Null(hit);
    }

    [Fact]
    public async Task GetByIdAsync_WithKnownId_ReturnsEntity()
    {
        MarkdownFile storedFile = await CreateStoredFileAsync(@"C:\Notes\eins.md", "eins.md", "eins");

        MarkdownFile? hit = await _sut.GetByIdAsync(storedFile.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(hit);
        Assert.Equal(storedFile.AbsolutePath, hit!.AbsolutePath, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetByAbsolutePathAsync_IgnoresCase()
    {
        _ = await CreateStoredFileAsync(@"C:\Notes\Titel.md", "Titel.md", "Titel");

        MarkdownFile? hit = await _sut.GetByAbsolutePathAsync(@"c:\notes\titel.md", TestContext.Current.CancellationToken);

        Assert.NotNull(hit);
    }

    [Fact]
    public async Task GetByAbsolutePathAsync_WithoutPath_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.GetByAbsolutePathAsync("  ", TestContext.Current.CancellationToken));

    [Fact]
    public async Task GetAllUnderRootAsync_DoesNotReachIntoSimilarlyNamedSibling()
    {
        _ = await CreateStoredFileAsync(@"C:\Notes\inside.md", "inside.md", "inside");
        _ = await CreateStoredFileAsync(@"C:\Notes-evil\outside.md", "outside.md", "outside");

        IReadOnlyList<MarkdownFile> hits = await _sut.GetAllUnderRootAsync(@"C:\Notes", TestContext.Current.CancellationToken);

        _ = Assert.Single(hits);
        Assert.Equal(@"C:\Notes\inside.md", hits[0].AbsolutePath, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetAllUnderRootAsync_WithTrailingSeparator_ReturnsSameResult()
    {
        _ = await CreateStoredFileAsync(@"C:\Notes\eins.md", "eins.md", "eins");

        IReadOnlyList<MarkdownFile> withoutSeparator = await _sut.GetAllUnderRootAsync(@"C:\Notes", TestContext.Current.CancellationToken);
        IReadOnlyList<MarkdownFile> withSeparator = await _sut.GetAllUnderRootAsync(@"C:\Notes\", TestContext.Current.CancellationToken);

        Assert.Equal(withoutSeparator.Count, withSeparator.Count);
    }

    [Fact]
    public async Task GetAllUnderRootAsync_WithoutRoot_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.GetAllUnderRootAsync("   ", TestContext.Current.CancellationToken));

    [Fact]
    public async Task CountAsync_CountsAllEntries()
    {
        Assert.Equal(0, await _sut.CountAsync(TestContext.Current.CancellationToken));

        _ = await CreateStoredFileAsync(@"C:\Notes\a.md", "a.md", "a");
        _ = await CreateStoredFileAsync(@"C:\Notes\b.md", "b.md", "b");

        Assert.Equal(2, await _sut.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddAsync_WithoutEntity_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.AddAsync(null!, TestContext.Current.CancellationToken));

    [Fact]
    public void Update_WithoutEntity_Throws() =>
        Assert.Throws<ArgumentNullException>(() => _sut.Update(null!));

    [Fact]
    public void Remove_WithoutEntity_Throws() =>
        Assert.Throws<ArgumentNullException>(() => _sut.Remove(null!));

    [Fact]
    public async Task Update_PersistsChangedValues()
    {
        MarkdownFile storedFile = await CreateStoredFileAsync(@"C:\Notes\wandelbar.md", "wandelbar.md", "wandelbar");
        storedFile.SizeBytes = 4711;

        _sut.Update(storedFile);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken);

        MarkdownFile? reloaded = await _sut.GetByIdAsync(storedFile.Id, TestContext.Current.CancellationToken);
        Assert.Equal(4711, reloaded!.SizeBytes);
    }

    [Fact]
    public async Task Remove_WithTrackedEntity_DeletesTheRow()
    {
        MarkdownFile storedFile = await CreateStoredFileAsync(@"C:\Notes\weg.md", "weg.md", "weg");

        _sut.Remove(storedFile);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, await _sut.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Remove_WithDetachedCopy_DeletesTheRow()
    {
        // GetAllUnderRootAsync liefert nicht verfolgte Entitäten. Wer eine davon entfernen
        // will, übergibt genau so eine Kopie, und das muss funktionieren, ohne dass der Aufrufer
        // sich um den Änderungsverfolger kümmern muss.
        MarkdownFile created = await CreateStoredFileAsync(@"C:\Notes\lose.md", "lose.md", "lose");
        _dbContext.ChangeTracker.Clear();
        IReadOnlyList<MarkdownFile> loaded = await _sut.GetAllUnderRootAsync(@"C:\Notes", TestContext.Current.CancellationToken);
        MarkdownFile detached = Assert.Single(loaded);
        Assert.Equal(created.Id, detached.Id);

        _sut.Remove(detached);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, await _sut.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Remove_WhenAnotherCopyIsAlreadyTracked_DoesNotConflict()
    {
        // Der Aufrufer hat die Entität schon geladen und übergibt eine zweite Instanz mit
        // derselben Id. Ein blindes Attach würde hier mit einem Identitätskonflikt scheitern.
        MarkdownFile trackedFile = await CreateStoredFileAsync(@"C:\Notes\zwilling.md", "zwilling.md", "zwilling");
        MarkdownFile secondInstance = new()
        {
            Id = trackedFile.Id,
            AbsolutePath = trackedFile.AbsolutePath,
            RelativePath = trackedFile.RelativePath,
            FileNameWithoutExtension = trackedFile.FileNameWithoutExtension,
            ContentHash = trackedFile.ContentHash,
        };

        _sut.Remove(secondInstance);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, await _sut.CountAsync(TestContext.Current.CancellationToken));
    }

    private async Task<MarkdownFile> CreateStoredFileAsync(string absolutePathOfFile, string relativePathOfFile, string nameWithoutExtension)
    {
        MarkdownFile storedFile = new()
        {
            Id = _ids.Next(),
            AbsolutePath = absolutePathOfFile,
            RelativePath = relativePathOfFile,
            FileNameWithoutExtension = nameWithoutExtension,
            ContentHash = "0000",
            IndexedAtUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            LastWriteTimeUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        await _sut.AddAsync(storedFile, TestContext.Current.CancellationToken).ConfigureAwait(false);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        return storedFile;
    }
    /// <summary>
    /// Ein Aufräumdurchgang über einen gewachsenen Bestand muss durchlaufen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Durchsucht <c>Remove</c> für jede Entfernung die Liste der bereits vorgemerkten Entitäten
    /// von vorn, sind das bei n Entfernungen n²/2 Vergleiche. Bei 33.886 Einträgen, davon rund
    /// 27.000 für Dateien, die es nicht mehr gibt, liefe der Aufräumdurchgang 20 Minuten unter
    /// voller Rechenlast und schriebe in dieser Zeit keine einzige Zeile.
    /// </para>
    /// <para>
    /// Dieser Test läuft über 3.000 Einträge, genug, damit ein quadratischer Weg spürbar
    /// wird, und wenig genug, dass die Suite schnell bleibt. Er prüft vor allem, dass am Ende
    /// wirklich alle Zeilen weg sind. Eine Zusicherung auf Sekunden fehlt mit Absicht: Sie fiele
    /// unter paralleler Ausführung um und sagte über die Ordnung nichts aus.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Remove_OverAGrownStock_RemovesEveryEntry()
    {
        const int FileCount = 3_000;
        for (int i = 0; i < FileCount; i++)
        {
            _ = await _dbContext.Set<MarkdownFile>().AddAsync(StoredFile(i), TestContext.Current.CancellationToken).ConfigureAwait(true);
        }
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        _dbContext.ChangeTracker.Clear();

        IReadOnlyList<MarkdownFile> saved = await _sut
            .GetAllUnderRootAsync(@"C:\bestand", TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(FileCount, saved.Count);

        foreach (MarkdownFile entryToRemove in saved)
        {
            _sut.Remove(entryToRemove);
        }
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Empty(await _sut.GetAllUnderRootAsync(@"C:\bestand", TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    private static MarkdownFile StoredFile(int index)
    {
        string name = index.ToString("D5", System.Globalization.CultureInfo.InvariantCulture);
        return new MarkdownFile
        {
            Id = new Guid(name.PadLeft(8, '0') + "-0000-0000-0000-000000000000"),
            AbsolutePath = @"C:\bestand\" + name + ".md",
            RelativePath = name + ".md",
            FileNameWithoutExtension = name,
            SizeBytes = 0,
            LastWriteTimeUtc = new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc),
            ContentHash = "hash-" + name,
            IndexedAtUtc = new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc),
        };
    }
    /// <summary>
    /// Die Mengen-Löschung nimmt mit, was an den Einträgen hängt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Der Punkt, an dem dieser Weg gefährlich wäre: Er geht an der Änderungsverfolgung vorbei
    /// und setzt die Löschung direkt ab. Was über Fremdschlüssel an einer Datei hängt (das
    /// übersetzte Dokument, die Zuordnung zu Schlagworten, der Fehlschlag-Vermerk) verschwindet
    /// damit nicht mehr, weil das Rahmenwerk es mitzieht, sondern weil die Datenbank es tut. Den
    /// Volltext-Eintrag räumt keiner von beiden ab, sondern der nächste Abgleich des Suchindex.
    /// </para>
    /// <para>
    /// Das prüft dieser Test. Wäre die Weitergabe nur im Rahmenwerk hinterlegt und nicht
    /// im Schema, bliebe hier ein Dokument ohne Datei zurück, und die Suche fände es weiter.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RemoveRangeAsync_AlsoRemovesWhatHangsOnTheEntries()
    {
        MarkdownFile kept = StoredFile(1);
        MarkdownFile dropped = StoredFile(2);
        _ = await _dbContext.Set<MarkdownFile>().AddAsync(kept, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _dbContext.Set<MarkdownFile>().AddAsync(dropped, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _dbContext.Set<MarkdownDocument>().AddAsync(StoredDocument(dropped.Id), TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _dbContext.Set<MarkdownDocument>().AddAsync(StoredDocument(kept.Id), TestContext.Current.CancellationToken).ConfigureAwait(true);
        Tag tag = new() { Id = _ids.Next(), Name = "Alpha", Slug = "alpha" };
        _ = await _dbContext.Set<Tag>().AddAsync(tag, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _dbContext.Set<MarkdownFileTag>().AddAsync(new MarkdownFileTag { MarkdownFileId = dropped.Id, TagId = tag.Id }, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _dbContext.Set<ParseFailure>().AddAsync(StoredFailure(dropped.Id), TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        _dbContext.ChangeTracker.Clear();

        int removedCount = await _sut.RemoveRangeAsync([dropped.Id], TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(1, removedCount);
        MarkdownFile stillStored = Assert.Single(
            await _sut.GetAllUnderRootAsync(@"C:\bestand", TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal(kept.Id, stillStored.Id);
        List<Guid> remainingDocuments = await _dbContext.Set<MarkdownDocument>()
            .AsNoTracking().Select(storedDocument => storedDocument.MarkdownFileId).ToListAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(kept.Id, Assert.Single(remainingDocuments));
        Assert.Equal(0, await _dbContext.Set<MarkdownFileTag>().CountAsync(TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal(0, await _dbContext.Set<ParseFailure>().CountAsync(TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Fact]
    public async Task RemoveRangeAsync_LeavesOtherPendingChangesInTheContext()
    {
        // Der Kontext ist je Bereich geteilt. Verwürfe die Löschung den ganzen Änderungsverfolger,
        // gingen ungespeicherte Änderungen anderer Repositories im selben Bereich still verloren.
        MarkdownFile dropped = StoredFile(3);
        _ = await _dbContext.Set<MarkdownFile>().AddAsync(dropped, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        Tag pending = new() { Id = _ids.Next(), Name = "Offen", Slug = "offen" };
        _ = await _dbContext.Set<Tag>().AddAsync(pending, TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = await _sut.RemoveRangeAsync([dropped.Id], TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(await _dbContext.Set<Tag>().AnyAsync(tag => tag.Slug == "offen", TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal(EntityState.Detached, _dbContext.Entry(dropped).State);
    }

    /// <remarks>
    /// Mehr Schlüssel, als in eine Anweisung passen: Der Weg zerlegt sie in Portionen zu 500. Die
    /// 1.200 ergeben zwei volle Portionen und eine angebrochene; an deren Grenzen bliebe ein
    /// Abschneidefehler sonst unbemerkt.
    /// </remarks>
    [Fact]
    public async Task RemoveRangeAsync_AcrossMoreEntriesThanOneStatementHolds_RemovesEveryOne()
    {
        const int FileCount = 1_200;
        List<Guid> ids = [];
        // Ab eins: Der Nullwert ergäbe einen leeren Schlüssel, und den ersetzt das Rahmenwerk
        // beim Anlegen durch einen eigenen; die Liste zeigte danach ins Leere.
        for (int i = 1; i <= FileCount; i++)
        {
            MarkdownFile storedFile = StoredFile(i);
            ids.Add(storedFile.Id);
            _ = await _dbContext.Set<MarkdownFile>().AddAsync(storedFile, TestContext.Current.CancellationToken).ConfigureAwait(true);
        }
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        _dbContext.ChangeTracker.Clear();

        int removedCount = await _sut.RemoveRangeAsync(ids, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(FileCount, removedCount);
        Assert.Empty(await _sut.GetAllUnderRootAsync(@"C:\bestand", TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Fact]
    public async Task RemoveRangeAsync_WithoutAnyKey_DoesNothing()
    {
        Assert.Equal(0, await _sut.RemoveRangeAsync([], TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Fact]
    public async Task RemoveRangeAsync_WithoutAList_Throws()
    {
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.RemoveRangeAsync(null!, TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    private ParseFailure StoredFailure(Guid storedFileId) => new()
    {
        Id = _ids.Next(),
        MarkdownFileId = storedFileId,
        ContentHash = "hash",
        EngineVersion = "test-engine/1",
        FailureReason = "ArgumentException: depth limit exceeded",
        FailedAtUtc = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc),
    };

    private MarkdownDocument StoredDocument(Guid storedFileId) => new()
    {
        Id = _ids.Next(),
        MarkdownFileId = storedFileId,
        SourceContentHash = "hash",
        FrontmatterJson = "{}",
        OutlinksJson = "[]",
        ParsedAtUtc = new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc),
    };
}
