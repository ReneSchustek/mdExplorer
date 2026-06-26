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
/// kein SQLite-Parameter-Limit ausloest.
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

        // Sollte ohne SqliteException "too many SQL variables" zurueckkommen — alle Ids fehlen in der leeren DB.
        IReadOnlyList<Guid> result = await _documentRepository
            .GetStaleOrMissingAsync(hashes, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(BatchSize, result.Count);
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
