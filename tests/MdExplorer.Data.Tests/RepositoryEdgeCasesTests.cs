using System.Data.Common;
using MdExplorer.Core.Models;
using MdExplorer.Data.Repositories;
using MdExplorer.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Tests;

/// <summary>
/// Die EF-Core-Repositories behandeln Randfälle gegen eine In-Memory-SQLite verlässlich: Leere
/// Eingaben liefern leere Ergebnisse, <c>null</c> wirft sofort, und doppelte Slugs, Pfade oder
/// Zuordnungen scheitern laut, statt eine zweite Zeile anzulegen.
/// </summary>
public sealed class RepositoryEdgeCasesTests : IAsyncDisposable
{
    private readonly TestIdSequence _ids = new();
    private readonly SqliteConnection _connection;
    private readonly MdExplorerDbContext _dbContext;
    private readonly TagRepository _tagRepository;
    private readonly MarkdownDocumentRepository _documentRepository;
    private readonly MarkdownFileRepository _fileRepository;

    public RepositoryEdgeCasesTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<MdExplorerDbContext> options = new DbContextOptionsBuilder<MdExplorerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

        _dbContext = new MdExplorerDbContext(options);
        _ = _dbContext.Database.EnsureCreated();
        _tagRepository = new TagRepository(_dbContext);
        _documentRepository = new MarkdownDocumentRepository(_dbContext);
        _fileRepository = new MarkdownFileRepository(_dbContext);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task TagRepo_GetBySlugsAsync_OnEmptyCollection_ReturnsEmpty()
    {
        IReadOnlyList<Tag> result = await _tagRepository
            .GetBySlugsAsync([], TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Empty(result);
    }

    [Fact]
    public async Task TagRepo_GetBySlugsAsync_OnNullArgument_Throws()
    {
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _tagRepository.GetBySlugsAsync(null!, TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    [Fact]
    public async Task TagRepo_AddAsync_OnNullArgument_Throws()
    {
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _tagRepository.AddAsync(null!, TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    [Fact]
    public async Task TagRepo_ReplaceFileTagsAsync_OnNullArgument_Throws()
    {
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _tagRepository.ReplaceFileTagsAsync(null!, TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    [Fact]
    public async Task DocRepo_GetStaleOrMissingAsync_OnEmptyMap_ReturnsEmpty()
    {
        IReadOnlyList<Guid> result = await _documentRepository
            .GetStaleOrMissingAsync(new Dictionary<Guid, string>(), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Empty(result);
    }

    [Fact]
    public async Task DocRepo_GetStaleOrMissingAsync_OnNullArgument_Throws()
    {
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _documentRepository.GetStaleOrMissingAsync(null!, TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    [Fact]
    public async Task TagRepo_AddAsync_OnDuplicateSlug_ThrowsOnSaveChanges()
    {
        Tag first = new() { Id = _ids.Next(), Name = "Foo", Slug = "foo" };
        await _tagRepository.AddAsync(first, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _tagRepository.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Tag duplicate = new() { Id = _ids.Next(), Name = "Foo-2", Slug = "foo" };
        await _tagRepository.AddAsync(duplicate, TestContext.Current.CancellationToken).ConfigureAwait(true);

        DatabaseOperationException ex = await Assert.ThrowsAsync<DatabaseOperationException>(
            () => _tagRepository.SaveChangesAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true);
        DbUpdateException update = Assert.IsType<DbUpdateException>(ex.InnerException);
        _ = Assert.IsType<SqliteException>(update.InnerException);
    }

    [Fact]
    public async Task TagRepo_AddAsync_OnSameSlugInSamePending_ThrowsOnSaveChanges()
    {
        // Beide Zeilen gehen im selben SaveChanges an die Datenbank; erst der eindeutige Index fängt sie ab.
        Tag a = new() { Id = _ids.Next(), Name = "Foo", Slug = "duplicate-pending" };
        Tag b = new() { Id = _ids.Next(), Name = "Foo-2", Slug = "duplicate-pending" };

        await _tagRepository.AddAsync(a, TestContext.Current.CancellationToken).ConfigureAwait(true);
        await _tagRepository.AddAsync(b, TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = await Assert.ThrowsAsync<DatabaseOperationException>(
            () => _tagRepository.SaveChangesAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    [Fact]
    public async Task TagRepo_ReplaceFileTagsAsync_OnEmptyTagIds_RemovesAllExisting()
    {
        Guid fileId = await SeedFileWithTwoTagsAsync().ConfigureAwait(true);

        await ReplaceAsync(fileId, []).ConfigureAwait(true);
        _ = await _tagRepository.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        int remaining = await _dbContext.Set<MarkdownFileTag>()
            .CountAsync(link => link.MarkdownFileId == fileId, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(0, remaining);
    }

    [Fact]
    public async Task TagRepo_ReplaceFileTagsAsync_WithOverlappingTags_KeepsTheSharedLinkAndSwapsTheRest()
    {
        // Der häufigste Fall im Betrieb: Eine Datei wird neu geparst, und ein Teil ihrer
        // Schlagworte bleibt. Wie im Parser läuft das in einem frischen Kontext, der die alten
        // Zuordnungen nicht kennt.
        Guid fileId = await SeedFileWithTwoTagsAsync().ConfigureAwait(true);
        Guid beta = await TagIdAsync("beta").ConfigureAwait(true);
        Tag gamma = new() { Id = _ids.Next(), Name = "Gamma", Slug = "gamma" };
        _ = _dbContext.Set<Tag>().Add(gamma);
        _ = await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        _dbContext.ChangeTracker.Clear();

        await ReplaceAsync(fileId, [beta, gamma.Id]).ConfigureAwait(true);
        _ = await _tagRepository.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        List<Guid> linked = await _dbContext.Set<MarkdownFileTag>()
            .Where(link => link.MarkdownFileId == fileId)
            .Select(link => link.TagId)
            .ToListAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal([beta, gamma.Id], linked.Order());
    }

    [Fact]
    public async Task TagRepo_ReplaceFileTagsAsync_OnDuplicateTagIdsInInput_LinksTheTagOnce()
    {
        // Ein Schlagwort, das in einer Datei zweimal vorkommt, ist eine Zuordnung. Der Schlüssel
        // {MarkdownFileId, TagId} ließe eine zweite ohnehin nicht zu.
        Guid fileId = await SeedFileWithTwoTagsAsync().ConfigureAwait(true);
        Guid alpha = await TagIdAsync("alpha").ConfigureAwait(true);
        _dbContext.ChangeTracker.Clear();

        await ReplaceAsync(fileId, [alpha, alpha]).ConfigureAwait(true);
        _ = await _tagRepository.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Guid linked = await _dbContext.Set<MarkdownFileTag>()
            .Where(link => link.MarkdownFileId == fileId)
            .Select(link => link.TagId)
            .SingleAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(alpha, linked);
    }

    [Fact]
    public async Task FileRepo_GetByAbsolutePathAsync_OnNonExistentPath_ReturnsNull()
    {
        MarkdownFile? result = await _fileRepository
            .GetByAbsolutePathAsync(@"C:\nicht\vorhanden.md", TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Null(result);
    }

    [Fact]
    public async Task FileRepo_AddAsync_OnDuplicateAbsolutePath_ThrowsOnSaveChanges()
    {
        MarkdownFile a = NewFile(@"C:\notes\a.md");
        await _fileRepository.AddAsync(a, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _fileRepository.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        MarkdownFile dup = NewFile(@"C:\notes\a.md");
        await _fileRepository.AddAsync(dup, TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = await Assert.ThrowsAsync<DatabaseOperationException>(
            () => _fileRepository.SaveChangesAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    private Task<Guid> TagIdAsync(string slug) =>
        _dbContext.Set<Tag>().Where(tag => tag.Slug == slug).Select(tag => tag.Id).SingleAsync(TestContext.Current.CancellationToken);

    private Task ReplaceAsync(Guid fileId, IReadOnlyCollection<Guid> tagIds) =>
        _tagRepository.ReplaceFileTagsAsync(new Dictionary<Guid, IReadOnlyCollection<Guid>> { [fileId] = tagIds }, TestContext.Current.CancellationToken);

    private async Task<Guid> SeedFileWithTwoTagsAsync()
    {
        MarkdownFile file = NewFile(@"C:\seed\file.md");
        _ = _dbContext.Set<MarkdownFile>().Add(file);

        Tag tag1 = new() { Id = _ids.Next(), Name = "Alpha", Slug = "alpha" };
        Tag tag2 = new() { Id = _ids.Next(), Name = "Beta", Slug = "beta" };
        _ = _dbContext.Set<Tag>().Add(tag1);
        _ = _dbContext.Set<Tag>().Add(tag2);
        _ = _dbContext.Set<MarkdownFileTag>().Add(new MarkdownFileTag { MarkdownFileId = file.Id, TagId = tag1.Id });
        _ = _dbContext.Set<MarkdownFileTag>().Add(new MarkdownFileTag { MarkdownFileId = file.Id, TagId = tag2.Id });
        _ = await _dbContext.SaveChangesAsync().ConfigureAwait(true);
        return file.Id;
    }

    private MarkdownFile NewFile(string absolutePath) => new()
    {
        Id = _ids.Next(),
        AbsolutePath = absolutePath,
        RelativePath = System.IO.Path.GetFileName(absolutePath),
        FileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(absolutePath),
        SizeBytes = 0,
        LastWriteTimeUtc = DateTime.UnixEpoch,
        ContentHash = "h",
        IndexedAtUtc = DateTime.UnixEpoch,
    };
}
