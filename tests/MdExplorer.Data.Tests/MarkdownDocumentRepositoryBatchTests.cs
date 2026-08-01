using MdExplorer.Core.Models;
using MdExplorer.Data;
using MdExplorer.Data.Repositories;
using MdExplorer.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Tests;

/// <summary>
/// Stellt sicher, dass ein 2.000-File-Batch in
/// <see cref="MarkdownDocumentRepository.GetStaleOrMissingAsync"/>
/// kein SQLite-Parameter-Limit auslöst.
/// </summary>
public sealed class MarkdownDocumentRepositoryBatchTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MdExplorerDbContext _dbContext;
    private readonly MarkdownDocumentRepository _documentRepository;
    private readonly TagRepository _tagRepository;

    public MarkdownDocumentRepositoryBatchTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<MdExplorerDbContext> options = new DbContextOptionsBuilder<MdExplorerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

        _dbContext = new MdExplorerDbContext(options);
        _ = _dbContext.Database.EnsureCreated();
        _documentRepository = new MarkdownDocumentRepository(_dbContext);
        _tagRepository = new TagRepository(_dbContext);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task GetStaleOrMissingAsync_OnTwoThousandIds_DoesNotExceedSqliteParameterLimit()
    {
        const int BatchSize = 2_000;
        Dictionary<Guid, string> hashes = new(BatchSize);
        for (int i = 0; i < BatchSize; i++)
        {
            hashes[Guid.NewGuid()] = $"hash-{i}";
        }

        // Sollte ohne SqliteException "too many SQL variables" zurückkommen — alle Ids fehlen in der leeren DB.
        IReadOnlyList<Guid> result = await _documentRepository
            .GetStaleOrMissingAsync(hashes, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(BatchSize, result.Count);
    }

    [Fact]
    public async Task GetByMarkdownFileIdsAsync_OnTwoThousandIds_DoesNotExceedSqliteParameterLimit()
    {
        const int BatchSize = 2_000;
        List<Guid> ids = new(BatchSize);
        for (int i = 0; i < BatchSize; i++)
        {
            ids.Add(Guid.NewGuid());
        }

        // Alle Ids fehlen in der leeren DB — darf ohne SqliteException "too many SQL variables" zurückkommen.
        IReadOnlyDictionary<Guid, MarkdownDocument> result = await _documentRepository
            .GetByMarkdownFileIdsAsync(ids, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetByMarkdownFileIdsAsync_ReturnsTrackedEntities_ThatPersistUpdates()
    {
        Guid fileId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        MarkdownFile parent = new()
        {
            Id = fileId,
            AbsolutePath = @"C:\V\doc.md",
            RelativePath = "doc.md",
            FileNameWithoutExtension = "doc",
            SizeBytes = 0,
            LastWriteTimeUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            ContentHash = "file-hash",
            IndexedAtUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        _ = await _dbContext.Set<MarkdownFile>().AddAsync(parent).ConfigureAwait(true);
        _ = await _dbContext.SaveChangesAsync().ConfigureAwait(true);

        MarkdownDocument seed = new()
        {
            Id = Guid.NewGuid(),
            MarkdownFileId = fileId,
            SourceContentHash = "alt",
            FrontmatterJson = "{}",
            OutlinksJson = "[]",
            ParsedAtUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        seed.SetRenderedHtmlGz([1, 2, 3]);
        await _documentRepository.AddAsync(seed, CancellationToken.None).ConfigureAwait(true);
        _ = await _documentRepository.SaveChangesAsync(CancellationToken.None).ConfigureAwait(true);

        IReadOnlyDictionary<Guid, MarkdownDocument> loaded = await _documentRepository
            .GetByMarkdownFileIdsAsync([fileId], CancellationToken.None)
            .ConfigureAwait(true);

        MarkdownDocument tracked = loaded[fileId];
        tracked.SourceContentHash = "neu";
        _documentRepository.Update(tracked);
        _ = await _documentRepository.SaveChangesAsync(CancellationToken.None).ConfigureAwait(true);

        MarkdownDocument? reloaded = await _documentRepository
            .GetByMarkdownFileIdAsync(fileId, CancellationToken.None)
            .ConfigureAwait(true);
        Assert.NotNull(reloaded);
        Assert.Equal("neu", reloaded.SourceContentHash);
    }

    [Fact]
    public async Task GetBySlugsAsync_OnTwoThousandSlugs_DoesNotExceedSqliteParameterLimit()
    {
        const int BatchSize = 2_000;
        List<string> slugs = new(BatchSize);
        for (int i = 0; i < BatchSize; i++)
        {
            slugs.Add($"tag-{i:D4}");
        }

        IReadOnlyList<Tag> result = await _tagRepository
            .GetBySlugsAsync(slugs, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Empty(result);
    }
}
