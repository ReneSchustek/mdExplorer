using MdExplorer.Core.Models;
using MdExplorer.Data;
using MdExplorer.Data.Repositories;
using MdExplorer.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Indexer.Tests.Integration;

/// <summary>
/// Integrationstest gegen eine In-Memory-SQLite-Verbindung. Verifiziert das
/// Fluent-API-Mapping (<c>MarkdownFileConfiguration</c>) zusammen mit dem
/// produktiven <c>MarkdownFileRepository</c>.
/// </summary>
public sealed class MarkdownFileRepositoryIntegrationTests : IAsyncDisposable
{
    private static readonly DateTime FixedIndexedAt = new(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid FixedId = new("11111111-1111-1111-1111-111111111111");

    private readonly SqliteConnection _connection;
    private readonly MdExplorerDbContext _dbContext;
    private readonly MarkdownFileRepository _sut;

    public MarkdownFileRepositoryIntegrationTests()
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
    public async Task AddAsync_ThenSave_RoundTripsAllProperties()
    {
        MarkdownFile entity = NewFile(@"C:\Wurzel\a.md", "Inhalt-Hash-1");

        await _sut.AddAsync(entity, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        MarkdownFile? loaded = await _sut.GetByAbsolutePathAsync(@"C:\Wurzel\a.md", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.NotNull(loaded);
        Assert.Equal(entity.Id, loaded.Id);
        Assert.Equal(entity.AbsolutePath, loaded.AbsolutePath);
        Assert.Equal(entity.RelativePath, loaded.RelativePath);
        Assert.Equal(entity.FileNameWithoutExtension, loaded.FileNameWithoutExtension);
        Assert.Equal(entity.SizeBytes, loaded.SizeBytes);
        Assert.Equal(entity.ContentHash, loaded.ContentHash);
        Assert.Equal(entity.LastWriteTimeUtc, loaded.LastWriteTimeUtc);
        Assert.Equal(entity.IndexedAtUtc, loaded.IndexedAtUtc);
    }

    [Fact]
    public async Task AddAsync_OnDuplicateAbsolutePath_ViolatesUniqueIndex()
    {
        MarkdownFile original = NewFile(@"C:\Wurzel\a.md", "Hash-1");
        await _sut.AddAsync(original, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        MarkdownFile duplicate = NewFile(@"C:\Wurzel\a.md", "Hash-2");
        duplicate.Id = Guid.NewGuid();
        await _sut.AddAsync(duplicate, TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = await Assert.ThrowsAsync<DbUpdateException>(
            () => _sut.SaveChangesAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    [Fact]
    public async Task GetByAbsolutePathAsync_OnDifferentCasing_FindsExistingRow()
    {
        // NOCASE-Spalte: der gespeicherte Pfad muss auch bei abweichender Casing gefunden werden —
        // sonst legt der Indexer-Upsert unter Windows ein Duplikat statt eines Updates an.
        MarkdownFile entity = NewFile(@"C:\Wurzel\Datei.md", "Hash");
        await _sut.AddAsync(entity, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        MarkdownFile? loaded = await _sut.GetByAbsolutePathAsync(@"c:\wurzel\datei.MD", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.NotNull(loaded);
        Assert.Equal(entity.Id, loaded.Id);
    }

    [Fact]
    public async Task AddAsync_OnCaseVariantDuplicatePath_ViolatesUniqueIndex()
    {
        MarkdownFile original = NewFile(@"C:\Wurzel\Datei.md", "Hash-1");
        await _sut.AddAsync(original, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        MarkdownFile duplicate = NewFile(@"C:\wurzel\datei.md", "Hash-2");
        duplicate.Id = Guid.NewGuid();
        await _sut.AddAsync(duplicate, TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = await Assert.ThrowsAsync<DbUpdateException>(
            () => _sut.SaveChangesAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    [Fact]
    public async Task GetAllUnderRootAsync_FiltersByPrefix()
    {
        await _sut.AddAsync(NewFile(@"C:\Wurzel\a.md", "h1"), TestContext.Current.CancellationToken).ConfigureAwait(true);
        await _sut.AddAsync(NewFile(@"C:\Wurzel\sub\b.md", "h2", new Guid("22222222-2222-2222-2222-222222222222")), TestContext.Current.CancellationToken).ConfigureAwait(true);
        await _sut.AddAsync(NewFile(@"D:\Andere\c.md", "h3", new Guid("33333333-3333-3333-3333-333333333333")), TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        IReadOnlyList<MarkdownFile> result = await _sut.GetAllUnderRootAsync(@"C:\Wurzel", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(2, result.Count);
        Assert.All(result, file => Assert.StartsWith(@"C:\Wurzel\", file.AbsolutePath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetAllUnderRootAsync_OnSimilarSiblingRoot_DoesNotMatchAcrossDirectoryBoundary()
    {
        await _sut.AddAsync(NewFile(@"C:\Notes\valid.md", "h-valid"), TestContext.Current.CancellationToken).ConfigureAwait(true);
        await _sut.AddAsync(NewFile(@"C:\Notes-evil\evil.md", "h-evil", new Guid("44444444-4444-4444-4444-444444444444")), TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        IReadOnlyList<MarkdownFile> result = await _sut.GetAllUnderRootAsync(@"C:\Notes", TestContext.Current.CancellationToken).ConfigureAwait(true);

        MarkdownFile single = Assert.Single(result);
        Assert.Equal(@"C:\Notes\valid.md", single.AbsolutePath);
    }

    [Fact]
    public async Task GetAllUnderRootAsync_OnRootWithTrailingSeparator_ReturnsSameResult()
    {
        await _sut.AddAsync(NewFile(@"C:\Wurzel\a.md", "h1"), TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        IReadOnlyList<MarkdownFile> result = await _sut.GetAllUnderRootAsync(@"C:\Wurzel\", TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = Assert.Single(result);
    }

    [Fact]
    public async Task Remove_ThenSave_DeletesEntity()
    {
        MarkdownFile entity = NewFile(@"C:\Wurzel\a.md", "Hash");
        await _sut.AddAsync(entity, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        _sut.Remove(entity);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        MarkdownFile? loaded = await _sut.GetByAbsolutePathAsync(@"C:\Wurzel\a.md", TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task Remove_ThenSave_CascadeDeletesDependentDocumentsAndJoinRows()
    {
        Guid tagId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        MarkdownFile entity = NewFile(@"C:\Wurzel\a.md", "Hash");
        await _sut.AddAsync(entity, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Tag tag = new() { Id = tagId, Name = "Beispiel", Slug = "beispiel" };
        _ = _dbContext.Set<Tag>().Add(tag);

        MarkdownDocument document = new()
        {
            Id = Guid.NewGuid(),
            MarkdownFileId = entity.Id,
            SourceContentHash = "doc-hash",
            FrontmatterJson = "{}",
            OutlinksJson = "[]",
            ParsedAtUtc = FixedIndexedAt,
        };
        document.SetRenderedHtmlGz([1, 2, 3]);
        _ = _dbContext.Set<MarkdownDocument>().Add(document);
        _ = _dbContext.Set<MarkdownFileTag>().Add(new MarkdownFileTag { MarkdownFileId = entity.Id, TagId = tagId });
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        _sut.Remove(entity);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Empty(_dbContext.Set<MarkdownDocument>().AsNoTracking().Where(d => d.MarkdownFileId == entity.Id));
        Assert.Empty(_dbContext.Set<MarkdownFileTag>().AsNoTracking().Where(link => link.MarkdownFileId == entity.Id));
        // Tag selbst bleibt — Cascade nur in eine Richtung (File → FileTag, nicht Tag → FileTag).
        Assert.NotNull(await _dbContext.Set<Tag>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == tagId, TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Fact]
    public async Task Delete_OnDetachedEntityFromAsNoTrackingQuery_StillRemoves()
    {
        MarkdownFile entity = NewFile(@"C:\Wurzel\detached.md", "Hash");
        await _sut.AddAsync(entity, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        IReadOnlyList<MarkdownFile> loaded = await _sut.GetAllUnderRootAsync(@"C:\Wurzel", TestContext.Current.CancellationToken).ConfigureAwait(true);
        MarkdownFile detached = Assert.Single(loaded);

        _sut.Remove(detached);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Null(await _sut.GetByAbsolutePathAsync(entity.AbsolutePath, TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    private static MarkdownFile NewFile(string absolutePath, string hash, Guid? id = null) => new()
    {
        Id = id ?? FixedId,
        AbsolutePath = absolutePath,
        RelativePath = Path.GetFileName(absolutePath),
        FileNameWithoutExtension = Path.GetFileNameWithoutExtension(absolutePath),
        SizeBytes = 42,
        LastWriteTimeUtc = FixedIndexedAt,
        ContentHash = hash,
        IndexedAtUtc = FixedIndexedAt,
    };
}
