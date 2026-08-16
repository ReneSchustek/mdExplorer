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
        MarkdownFile? treffer = await _sut.GetByIdAsync(Guid.Empty, TestContext.Current.CancellationToken);

        Assert.Null(treffer);
    }

    [Fact]
    public async Task GetByIdAsync_WithUnknownId_ReturnsNull()
    {
        MarkdownFile? treffer = await _sut.GetByIdAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Null(treffer);
    }

    [Fact]
    public async Task GetByIdAsync_WithKnownId_ReturnsEntity()
    {
        MarkdownFile datei = await AnlegenAsync(@"C:\Notes\eins.md", "eins.md", "eins");

        MarkdownFile? treffer = await _sut.GetByIdAsync(datei.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(treffer);
        Assert.Equal(datei.AbsolutePath, treffer!.AbsolutePath, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetByAbsolutePathAsync_IgnoresCase()
    {
        _ = await AnlegenAsync(@"C:\Notes\Titel.md", "Titel.md", "Titel");

        MarkdownFile? treffer = await _sut.GetByAbsolutePathAsync(@"c:\notes\titel.md", TestContext.Current.CancellationToken);

        Assert.NotNull(treffer);
    }

    [Fact]
    public async Task GetByAbsolutePathAsync_WithoutPath_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.GetByAbsolutePathAsync("  ", TestContext.Current.CancellationToken));

    [Fact]
    public async Task FindIdByFileNameAsync_IgnoresCase()
    {
        MarkdownFile datei = await AnlegenAsync(@"C:\Notes\Handbuch.md", "Handbuch.md", "Handbuch");

        Guid? treffer = await _sut.FindIdByFileNameAsync("handbuch", TestContext.Current.CancellationToken);

        Assert.Equal(datei.Id, treffer);
    }

    [Fact]
    public async Task FindIdByFileNameAsync_WithoutMatch_ReturnsNull()
    {
        _ = await AnlegenAsync(@"C:\Notes\vorhanden.md", "vorhanden.md", "vorhanden");

        Guid? treffer = await _sut.FindIdByFileNameAsync("gibt-es-nicht", TestContext.Current.CancellationToken);

        Assert.Null(treffer);
    }

    [Fact]
    public async Task FindIdByFileNameAsync_WithSeveralMatches_PrefersTheFirstRelativePath()
    {
        // Gleicher Dateiname in zwei Ordnern: Die Auswahl muss vorhersagbar sein,
        // sonst zeigt ein WikiLink mal hierhin, mal dorthin.
        _ = await AnlegenAsync(@"C:\Notes\zeta\doppelt.md", @"zeta\doppelt.md", "doppelt");
        MarkdownFile frueher = await AnlegenAsync(@"C:\Notes\alpha\doppelt.md", @"alpha\doppelt.md", "doppelt");

        Guid? treffer = await _sut.FindIdByFileNameAsync("doppelt", TestContext.Current.CancellationToken);

        Assert.Equal(frueher.Id, treffer);
    }

    [Fact]
    public async Task FindIdByFileNameAsync_WithoutName_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.FindIdByFileNameAsync(string.Empty, TestContext.Current.CancellationToken));

    [Fact]
    public async Task GetAllUnderRootAsync_DoesNotReachIntoSimilarlyNamedSibling()
    {
        _ = await AnlegenAsync(@"C:\Notes\innen.md", "innen.md", "innen");
        _ = await AnlegenAsync(@"C:\Notes-evil\aussen.md", "aussen.md", "aussen");

        IReadOnlyList<MarkdownFile> treffer = await _sut.GetAllUnderRootAsync(@"C:\Notes", TestContext.Current.CancellationToken);

        _ = Assert.Single(treffer);
        Assert.Equal(@"C:\Notes\innen.md", treffer[0].AbsolutePath, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetAllUnderRootAsync_WithTrailingSeparator_ReturnsSameResult()
    {
        _ = await AnlegenAsync(@"C:\Notes\eins.md", "eins.md", "eins");

        IReadOnlyList<MarkdownFile> ohne = await _sut.GetAllUnderRootAsync(@"C:\Notes", TestContext.Current.CancellationToken);
        IReadOnlyList<MarkdownFile> mit = await _sut.GetAllUnderRootAsync(@"C:\Notes\", TestContext.Current.CancellationToken);

        Assert.Equal(ohne.Count, mit.Count);
    }

    [Fact]
    public async Task GetAllUnderRootAsync_WithoutRoot_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.GetAllUnderRootAsync("   ", TestContext.Current.CancellationToken));

    [Fact]
    public async Task CountAsync_CountsAllEntries()
    {
        Assert.Equal(0, await _sut.CountAsync(TestContext.Current.CancellationToken));

        _ = await AnlegenAsync(@"C:\Notes\a.md", "a.md", "a");
        _ = await AnlegenAsync(@"C:\Notes\b.md", "b.md", "b");

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
        MarkdownFile datei = await AnlegenAsync(@"C:\Notes\wandelbar.md", "wandelbar.md", "wandelbar");
        datei.SizeBytes = 4711;

        _sut.Update(datei);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken);

        MarkdownFile? neu = await _sut.GetByIdAsync(datei.Id, TestContext.Current.CancellationToken);
        Assert.Equal(4711, neu!.SizeBytes);
    }

    [Fact]
    public async Task Remove_WithTrackedEntity_DeletesTheRow()
    {
        MarkdownFile datei = await AnlegenAsync(@"C:\Notes\weg.md", "weg.md", "weg");

        _sut.Remove(datei);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, await _sut.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Remove_WithDetachedCopy_DeletesTheRow()
    {
        // GetAllUnderRootAsync liefert nicht verfolgte Entitäten. Wer eine davon entfernen
        // will, übergibt genau so eine Kopie — das muss funktionieren, ohne dass der Aufrufer
        // sich um den Änderungsverfolger kümmern muss.
        MarkdownFile angelegt = await AnlegenAsync(@"C:\Notes\lose.md", "lose.md", "lose");
        _dbContext.ChangeTracker.Clear();
        IReadOnlyList<MarkdownFile> geladen = await _sut.GetAllUnderRootAsync(@"C:\Notes", TestContext.Current.CancellationToken);
        MarkdownFile lose = Assert.Single(geladen);
        Assert.Equal(angelegt.Id, lose.Id);

        _sut.Remove(lose);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, await _sut.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Remove_WhenAnotherCopyIsAlreadyTracked_DoesNotConflict()
    {
        // Der Aufrufer hat die Entität schon geladen und übergibt eine zweite Instanz mit
        // derselben Id. Ein blindes Attach würde hier mit einem Identitätskonflikt scheitern.
        MarkdownFile verfolgt = await AnlegenAsync(@"C:\Notes\zwilling.md", "zwilling.md", "zwilling");
        MarkdownFile zweiteInstanz = new()
        {
            Id = verfolgt.Id,
            AbsolutePath = verfolgt.AbsolutePath,
            RelativePath = verfolgt.RelativePath,
            FileNameWithoutExtension = verfolgt.FileNameWithoutExtension,
            ContentHash = verfolgt.ContentHash,
        };

        _sut.Remove(zweiteInstanz);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, await _sut.CountAsync(TestContext.Current.CancellationToken));
    }

    private async Task<MarkdownFile> AnlegenAsync(string absoluterPfad, string relativerPfad, string nameOhneEndung)
    {
        MarkdownFile datei = new()
        {
            Id = Guid.NewGuid(),
            AbsolutePath = absoluterPfad,
            RelativePath = relativerPfad,
            FileNameWithoutExtension = nameOhneEndung,
            ContentHash = "0000",
            IndexedAtUtc = DateTime.UtcNow,
            LastWriteTimeUtc = DateTime.UtcNow,
        };
        await _sut.AddAsync(datei, TestContext.Current.CancellationToken).ConfigureAwait(false);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        return datei;
    }
    /// <summary>
    /// Ein Aufräumdurchgang über einen gewachsenen Bestand muss durchlaufen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Am 16.08.2026 blieb genau das stehen: Der Index enthielt 33.886 Einträge, davon rund
    /// 27.000 für Dateien, die es nicht mehr gab. Der Aufräumdurchgang lief 20 Minuten mit
    /// voller Rechenlast und schrieb **keine einzige Zeile**. Ursache war diese Methode: Sie
    /// durchsuchte für jede Entfernung die Liste der bereits vorgemerkten Entitäten von vorne.
    /// Bei n Entfernungen sind das n²/2 Vergleiche — bei 27.000 rund 360 Millionen, und die
    /// Vormerkung selbst kostet noch einmal.
    /// </para>
    /// <para>
    /// Dieser Test läuft über 3.000 Einträge — genug, damit ein quadratischer Weg spürbar
    /// wird, und wenig genug, dass die Suite schnell bleibt. Er prüft vor allem, dass am Ende
    /// wirklich alle Zeilen weg sind. Eine Zusicherung auf Sekunden steht bewusst **nicht**
    /// hier: Sie fiele unter paralleler Ausführung um und sagte über die Ordnung nichts aus.
    /// </para>
    /// <para>
    /// Gemessen wurde einmalig mit 20.000 Einträgen: 2 Minuten 25 Sekunden für Anlegen und
    /// Entfernen zusammen — die Zeit steckt danach in SQLite, nicht mehr in der Suche.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Remove_OverAGrownStock_RemovesEveryEntry()
    {
        const int Anzahl = 3_000;
        for (int i = 0; i < Anzahl; i++)
        {
            _ = await _dbContext.Set<MarkdownFile>().AddAsync(Datei(i), TestContext.Current.CancellationToken).ConfigureAwait(true);
        }
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        _dbContext.ChangeTracker.Clear();

        IReadOnlyList<MarkdownFile> gespeichert = await _sut
            .GetAllUnderRootAsync(@"C:\bestand", TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(Anzahl, gespeichert.Count);

        foreach (MarkdownFile eintrag in gespeichert)
        {
            _sut.Remove(eintrag);
        }
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Empty(await _sut.GetAllUnderRootAsync(@"C:\bestand", TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    private static MarkdownFile Datei(int index)
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
    /// und setzt die Löschung direkt ab. Was an einer Datei hängt — das übersetzte Dokument,
    /// die Zuordnung zu Schlagworten, der Volltext-Eintrag — verschwindet damit nicht mehr,
    /// weil das Rahmenwerk es mitzieht, sondern weil die Datenbank es tut.
    /// </para>
    /// <para>
    /// Genau das prüft dieser Test. Wäre die Weitergabe nur im Rahmenwerk hinterlegt und nicht
    /// im Schema, bliebe hier ein Dokument ohne Datei zurück — und die Suche fände es weiter.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RemoveRangeAsync_AlsoRemovesWhatHangsOnTheEntries()
    {
        MarkdownFile bleibt = Datei(1);
        MarkdownFile geht = Datei(2);
        _ = await _dbContext.Set<MarkdownFile>().AddAsync(bleibt, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _dbContext.Set<MarkdownFile>().AddAsync(geht, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _dbContext.Set<MarkdownDocument>().AddAsync(Dokument(geht.Id), TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _dbContext.Set<MarkdownDocument>().AddAsync(Dokument(bleibt.Id), TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        _dbContext.ChangeTracker.Clear();

        int entfernt = await _sut.RemoveRangeAsync([geht.Id], TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(1, entfernt);
        MarkdownFile verblieben = Assert.Single(
            await _sut.GetAllUnderRootAsync(@"C:\bestand", TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal(bleibt.Id, verblieben.Id);
        List<Guid> verbliebeneDokumente = await _dbContext.Set<MarkdownDocument>()
            .AsNoTracking().Select(dokument => dokument.MarkdownFileId).ToListAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(bleibt.Id, Assert.Single(verbliebeneDokumente));
    }

    /// <remarks>
    /// Mehr Schlüssel, als in eine Anweisung passen: Der Weg zerlegt sie in Portionen. Der Test
    /// hält fest, dass dabei keiner verlorengeht — die Portionsgrenze ist genau die Stelle, an
    /// der ein Abschneidefehler unbemerkt bliebe.
    /// </remarks>
    [Fact]
    public async Task RemoveRangeAsync_AcrossMoreEntriesThanOneStatementHolds_RemovesEveryOne()
    {
        const int Anzahl = 1_200;
        List<Guid> ids = [];
        // Ab eins: Der Nullwert ergäbe einen leeren Schlüssel, und den ersetzt das Rahmenwerk
        // beim Anlegen durch einen eigenen — die Liste zeigte danach ins Leere.
        for (int i = 1; i <= Anzahl; i++)
        {
            MarkdownFile datei = Datei(i);
            ids.Add(datei.Id);
            _ = await _dbContext.Set<MarkdownFile>().AddAsync(datei, TestContext.Current.CancellationToken).ConfigureAwait(true);
        }
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        _dbContext.ChangeTracker.Clear();

        int entfernt = await _sut.RemoveRangeAsync(ids, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(Anzahl, entfernt);
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

    private static MarkdownDocument Dokument(Guid dateiId) => new()
    {
        Id = Guid.NewGuid(),
        MarkdownFileId = dateiId,
        SourceContentHash = "hash",
        FrontmatterJson = "{}",
        OutlinksJson = "[]",
        ParsedAtUtc = new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc),
    };
}
