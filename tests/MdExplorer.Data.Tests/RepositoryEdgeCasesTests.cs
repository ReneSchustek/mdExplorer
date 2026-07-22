using System.Data.Common;
using MdExplorer.Core.Models;
using MdExplorer.Data.Repositories;
using MdExplorer.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Tests;

/// <summary>
/// Edge-Case-Coverage fuer die EF-Core-Repositories. Integration-Tests
/// gegen In-Memory-SQLite — testen leere Inputs, Duplikat-Detection, NULL-Argumente
/// und ReplaceFileTagsAsync-Sonderfaelle.
/// </summary>
public sealed class RepositoryEdgeCasesTests : IAsyncDisposable
{
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

    // ============ Empty / NULL Inputs ============

    [Fact]
    public async Task TagRepo_GetBySlugsAsync_OnEmptyCollection_ReturnsEmpty()
    {
        IReadOnlyList<Tag> result = await _tagRepository
            .GetBySlugsAsync([], CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Empty(result);
    }

    [Fact]
    public async Task TagRepo_GetBySlugsAsync_OnNullArgument_Throws()
    {
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _tagRepository.GetBySlugsAsync(null!, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task TagRepo_AddAsync_OnNullArgument_Throws()
    {
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _tagRepository.AddAsync(null!, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task TagRepo_ReplaceFileTagsAsync_OnNullArgument_Throws()
    {
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _tagRepository.ReplaceFileTagsAsync(Guid.NewGuid(), null!, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task DocRepo_GetStaleOrMissingAsync_OnEmptyMap_ReturnsEmpty()
    {
        IReadOnlyList<Guid> result = await _documentRepository
            .GetStaleOrMissingAsync(new Dictionary<Guid, string>(), CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Empty(result);
    }

    [Fact]
    public async Task DocRepo_GetStaleOrMissingAsync_OnNullArgument_Throws()
    {
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _documentRepository.GetStaleOrMissingAsync(null!, CancellationToken.None)).ConfigureAwait(true);
    }

    // ============ Duplicate Detection ============

    [Fact]
    public async Task TagRepo_AddAsync_OnDuplicateSlug_ThrowsOnSaveChanges()
    {
        Tag first = new() { Id = Guid.NewGuid(), Name = "Foo", Slug = "foo" };
        await _tagRepository.AddAsync(first, CancellationToken.None).ConfigureAwait(true);
        _ = await _tagRepository.SaveChangesAsync(CancellationToken.None).ConfigureAwait(true);

        Tag duplicate = new() { Id = Guid.NewGuid(), Name = "Foo-2", Slug = "foo" };
        await _tagRepository.AddAsync(duplicate, CancellationToken.None).ConfigureAwait(true);

        DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(
            () => _tagRepository.SaveChangesAsync(CancellationToken.None)).ConfigureAwait(true);
        Assert.NotNull(ex.InnerException);
        _ = Assert.IsType<SqliteException>(ex.InnerException);
    }

    [Fact]
    public async Task TagRepo_AddAsync_OnSameSlugInSamePending_ThrowsOnSaveChanges()
    {
        // Repro: zwei Adds mit gleichem Slug im selben SaveChanges-Batch.
        Tag a = new() { Id = Guid.NewGuid(), Name = "Foo", Slug = "duplicate-pending" };
        Tag b = new() { Id = Guid.NewGuid(), Name = "Foo-2", Slug = "duplicate-pending" };

        await _tagRepository.AddAsync(a, CancellationToken.None).ConfigureAwait(true);
        await _tagRepository.AddAsync(b, CancellationToken.None).ConfigureAwait(true);

        _ = await Assert.ThrowsAsync<DbUpdateException>(
            () => _tagRepository.SaveChangesAsync(CancellationToken.None)).ConfigureAwait(true);
    }

    // ============ ReplaceFileTagsAsync edge cases ============

    [Fact]
    public async Task TagRepo_ReplaceFileTagsAsync_OnEmptyTagIds_RemovesAllExisting()
    {
        // Vorbereitung: ein File mit zwei Tag-Links.
        Guid fileId = await SeedFileWithTwoTagsAsync().ConfigureAwait(true);

        await _tagRepository.ReplaceFileTagsAsync(fileId, [], CancellationToken.None).ConfigureAwait(true);
        _ = await _tagRepository.SaveChangesAsync(CancellationToken.None).ConfigureAwait(true);

        int remaining = await _dbContext.Set<MarkdownFileTag>()
            .CountAsync(link => link.MarkdownFileId == fileId).ConfigureAwait(true);
        Assert.Equal(0, remaining);
    }

    [Fact]
    public async Task TagRepo_ReplaceFileTagsAsync_OnDuplicateTagIdsInInput_ThrowsDuringAdd()
    {
        // Befund: das Repo deduppt nicht. EF-Change-Tracker faengt das Duplikat
        // im Composite-Key {MarkdownFileId, TagId} bereits beim AddAsync ab. Caller muessen
        // dedupplizieren oder eine InvalidOperationException erwarten — dokumentiertes Verhalten.
        Guid fileId = await SeedFileWithTwoTagsAsync().ConfigureAwait(true);
        Guid singleTagId = await _dbContext.Set<Tag>().Select(t => t.Id).FirstAsync().ConfigureAwait(true);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _tagRepository.ReplaceFileTagsAsync(fileId, [singleTagId, singleTagId], CancellationToken.None))
            .ConfigureAwait(true);
    }

    // ============ MarkdownFileRepository ============

    [Fact]
    public async Task FileRepo_GetByAbsolutePathAsync_OnNonExistentPath_ReturnsNull()
    {
        MarkdownFile? result = await _fileRepository
            .GetByAbsolutePathAsync(@"C:\nicht\vorhanden.md", CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Null(result);
    }

    [Fact]
    public async Task FileRepo_AddAsync_OnDuplicateAbsolutePath_ThrowsOnSaveChanges()
    {
        MarkdownFile a = NewFile(@"C:\notes\a.md");
        await _fileRepository.AddAsync(a, CancellationToken.None).ConfigureAwait(true);
        _ = await _fileRepository.SaveChangesAsync(CancellationToken.None).ConfigureAwait(true);

        MarkdownFile dup = NewFile(@"C:\notes\a.md");
        await _fileRepository.AddAsync(dup, CancellationToken.None).ConfigureAwait(true);

        _ = await Assert.ThrowsAsync<DbUpdateException>(
            () => _fileRepository.SaveChangesAsync(CancellationToken.None)).ConfigureAwait(true);
    }

    // ============ Helfer ============

    private async Task<Guid> SeedFileWithTwoTagsAsync()
    {
        MarkdownFile file = NewFile(@"C:\seed\file.md");
        _ = _dbContext.Set<MarkdownFile>().Add(file);

        Tag tag1 = new() { Id = Guid.NewGuid(), Name = "Alpha", Slug = "alpha" };
        Tag tag2 = new() { Id = Guid.NewGuid(), Name = "Beta", Slug = "beta" };
        _ = _dbContext.Set<Tag>().Add(tag1);
        _ = _dbContext.Set<Tag>().Add(tag2);
        _ = _dbContext.Set<MarkdownFileTag>().Add(new MarkdownFileTag { MarkdownFileId = file.Id, TagId = tag1.Id });
        _ = _dbContext.Set<MarkdownFileTag>().Add(new MarkdownFileTag { MarkdownFileId = file.Id, TagId = tag2.Id });
        _ = await _dbContext.SaveChangesAsync().ConfigureAwait(true);
        return file.Id;
    }

    private static MarkdownFile NewFile(string absolutePath) => new()
    {
        Id = Guid.NewGuid(),
        AbsolutePath = absolutePath,
        RelativePath = System.IO.Path.GetFileName(absolutePath),
        FileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(absolutePath),
        SizeBytes = 0,
        LastWriteTimeUtc = DateTime.UnixEpoch,
        ContentHash = "h",
        IndexedAtUtc = DateTime.UnixEpoch,
    };
}
