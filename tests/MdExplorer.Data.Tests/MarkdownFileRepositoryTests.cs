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
        MarkdownFile? treffer = await _sut.GetByIdAsync(Guid.Empty, CancellationToken.None);

        Assert.Null(treffer);
    }

    [Fact]
    public async Task GetByIdAsync_WithUnknownId_ReturnsNull()
    {
        MarkdownFile? treffer = await _sut.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(treffer);
    }

    [Fact]
    public async Task GetByIdAsync_WithKnownId_ReturnsEntity()
    {
        MarkdownFile datei = await AnlegenAsync(@"C:\Notes\eins.md", "eins.md", "eins");

        MarkdownFile? treffer = await _sut.GetByIdAsync(datei.Id, CancellationToken.None);

        Assert.NotNull(treffer);
        Assert.Equal(datei.AbsolutePath, treffer!.AbsolutePath, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetByAbsolutePathAsync_IgnoresCase()
    {
        _ = await AnlegenAsync(@"C:\Notes\Titel.md", "Titel.md", "Titel");

        MarkdownFile? treffer = await _sut.GetByAbsolutePathAsync(@"c:\notes\titel.md", CancellationToken.None);

        Assert.NotNull(treffer);
    }

    [Fact]
    public async Task GetByAbsolutePathAsync_WithoutPath_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.GetByAbsolutePathAsync("  ", CancellationToken.None));

    [Fact]
    public async Task FindIdByFileNameAsync_IgnoresCase()
    {
        MarkdownFile datei = await AnlegenAsync(@"C:\Notes\Handbuch.md", "Handbuch.md", "Handbuch");

        Guid? treffer = await _sut.FindIdByFileNameAsync("handbuch", CancellationToken.None);

        Assert.Equal(datei.Id, treffer);
    }

    [Fact]
    public async Task FindIdByFileNameAsync_WithoutMatch_ReturnsNull()
    {
        _ = await AnlegenAsync(@"C:\Notes\vorhanden.md", "vorhanden.md", "vorhanden");

        Guid? treffer = await _sut.FindIdByFileNameAsync("gibt-es-nicht", CancellationToken.None);

        Assert.Null(treffer);
    }

    [Fact]
    public async Task FindIdByFileNameAsync_WithSeveralMatches_PrefersTheFirstRelativePath()
    {
        // Gleicher Dateiname in zwei Ordnern: Die Auswahl muss vorhersagbar sein,
        // sonst zeigt ein WikiLink mal hierhin, mal dorthin.
        _ = await AnlegenAsync(@"C:\Notes\zeta\doppelt.md", @"zeta\doppelt.md", "doppelt");
        MarkdownFile frueher = await AnlegenAsync(@"C:\Notes\alpha\doppelt.md", @"alpha\doppelt.md", "doppelt");

        Guid? treffer = await _sut.FindIdByFileNameAsync("doppelt", CancellationToken.None);

        Assert.Equal(frueher.Id, treffer);
    }

    [Fact]
    public async Task FindIdByFileNameAsync_WithoutName_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.FindIdByFileNameAsync(string.Empty, CancellationToken.None));

    [Fact]
    public async Task GetAllUnderRootAsync_DoesNotReachIntoSimilarlyNamedSibling()
    {
        _ = await AnlegenAsync(@"C:\Notes\innen.md", "innen.md", "innen");
        _ = await AnlegenAsync(@"C:\Notes-evil\aussen.md", "aussen.md", "aussen");

        IReadOnlyList<MarkdownFile> treffer = await _sut.GetAllUnderRootAsync(@"C:\Notes", CancellationToken.None);

        _ = Assert.Single(treffer);
        Assert.Equal(@"C:\Notes\innen.md", treffer[0].AbsolutePath, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetAllUnderRootAsync_WithTrailingSeparator_ReturnsSameResult()
    {
        _ = await AnlegenAsync(@"C:\Notes\eins.md", "eins.md", "eins");

        IReadOnlyList<MarkdownFile> ohne = await _sut.GetAllUnderRootAsync(@"C:\Notes", CancellationToken.None);
        IReadOnlyList<MarkdownFile> mit = await _sut.GetAllUnderRootAsync(@"C:\Notes\", CancellationToken.None);

        Assert.Equal(ohne.Count, mit.Count);
    }

    [Fact]
    public async Task GetAllUnderRootAsync_WithoutRoot_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.GetAllUnderRootAsync("   ", CancellationToken.None));

    [Fact]
    public async Task CountAsync_CountsAllEntries()
    {
        Assert.Equal(0, await _sut.CountAsync(CancellationToken.None));

        _ = await AnlegenAsync(@"C:\Notes\a.md", "a.md", "a");
        _ = await AnlegenAsync(@"C:\Notes\b.md", "b.md", "b");

        Assert.Equal(2, await _sut.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AddAsync_WithoutEntity_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.AddAsync(null!, CancellationToken.None));

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
        _ = await _sut.SaveChangesAsync(CancellationToken.None);

        MarkdownFile? neu = await _sut.GetByIdAsync(datei.Id, CancellationToken.None);
        Assert.Equal(4711, neu!.SizeBytes);
    }

    [Fact]
    public async Task Remove_WithTrackedEntity_DeletesTheRow()
    {
        MarkdownFile datei = await AnlegenAsync(@"C:\Notes\weg.md", "weg.md", "weg");

        _sut.Remove(datei);
        _ = await _sut.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(0, await _sut.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Remove_WithDetachedCopy_DeletesTheRow()
    {
        // GetAllUnderRootAsync liefert nicht verfolgte Entitäten. Wer eine davon entfernen
        // will, übergibt genau so eine Kopie — das muss funktionieren, ohne dass der Aufrufer
        // sich um den Änderungsverfolger kümmern muss.
        MarkdownFile angelegt = await AnlegenAsync(@"C:\Notes\lose.md", "lose.md", "lose");
        _dbContext.ChangeTracker.Clear();
        IReadOnlyList<MarkdownFile> geladen = await _sut.GetAllUnderRootAsync(@"C:\Notes", CancellationToken.None);
        MarkdownFile lose = Assert.Single(geladen);
        Assert.Equal(angelegt.Id, lose.Id);

        _sut.Remove(lose);
        _ = await _sut.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(0, await _sut.CountAsync(CancellationToken.None));
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
        _ = await _sut.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(0, await _sut.CountAsync(CancellationToken.None));
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
        await _sut.AddAsync(datei, CancellationToken.None).ConfigureAwait(false);
        _ = await _sut.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        return datei;
    }
}
