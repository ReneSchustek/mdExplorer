using MdExplorer.Core.Models;
using MdExplorer.Data;
using MdExplorer.Data.Repositories;
using MdExplorer.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Tests;

/// <summary>
/// Große Id- und Slug-Mengen gehen in Portionen an SQLite, damit eine IN-Liste die Grenze für
/// gebundene Parameter nicht reißt und die Abfrage mit „too many SQL variables" abbricht.
/// <para>
/// Die Verbindung läuft mit der alten Grenze von 999 Parametern (<see cref="SqliteParameterLimit"/>);
/// die 2.000 Ids liegen darüber. Ein fehlendes Portionieren fällt damit hier auf und nicht erst
/// beim Nutzer mit einer anders übersetzten SQLite.
/// </para>
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
        SqliteParameterLimit.Restrict(_connection);

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
        Dictionary<Guid, string> hashes = SqliteParameterLimit.DistinctIds(BatchSize)
            .ToDictionary(id => id, id => $"hash-{id}");

        // Alle Ids fehlen in der leeren DB und gelten deshalb als zu parsen.
        IReadOnlyList<Guid> result = await _documentRepository
            .GetStaleOrMissingAsync(hashes, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(BatchSize, result.Count);
    }

    [Fact]
    public async Task GetByMarkdownFileIdsAsync_OnTwoThousandIds_DoesNotExceedSqliteParameterLimit()
    {
        const int BatchSize = 2_000;
        List<Guid> ids = SqliteParameterLimit.DistinctIds(BatchSize);

        IReadOnlyDictionary<Guid, MarkdownDocument> result = await _documentRepository
            .GetByMarkdownFileIdsAsync(ids, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetByMarkdownFileIdsAsync_ReturnsTrackedEntities_ThatPersistUpdates()
    {
        Guid fileId = await SeedDocumentAsync().ConfigureAwait(true);

        IReadOnlyDictionary<Guid, MarkdownDocument> loaded = await _documentRepository
            .GetByMarkdownFileIdsAsync([fileId], TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        MarkdownDocument tracked = loaded[fileId];
        tracked.SourceContentHash = "neu";
        _documentRepository.Update(tracked);
        _ = await _documentRepository.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        MarkdownDocument? reloaded = await _documentRepository
            .GetByMarkdownFileIdAsync(fileId, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        Assert.NotNull(reloaded);
        Assert.Equal("neu", reloaded.SourceContentHash);
    }

    [Fact]
    public async Task GetByMarkdownFileIdAsync_LoadsWithoutTracking()
    {
        // Der einzige Leser ist die Vorschau. Verfolgt angelegt, hielte der Kontext eine Kopie des
        // ganzen gepackten HTML als Vergleichsstand, ohne dass je etwas zurückgeschrieben wird.
        Guid fileId = await SeedDocumentAsync().ConfigureAwait(true);
        _dbContext.ChangeTracker.Clear();

        MarkdownDocument? loaded = await _documentRepository
            .GetByMarkdownFileIdAsync(fileId, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.NotNull(loaded);
        Assert.Empty(_dbContext.ChangeTracker.Entries());
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
            .GetBySlugsAsync(slugs, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Empty(result);
    }

    private async Task<Guid> SeedDocumentAsync()
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
        _ = await _dbContext.Set<MarkdownFile>().AddAsync(parent, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        MarkdownDocument seed = new()
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            MarkdownFileId = fileId,
            SourceContentHash = "alt",
            FrontmatterJson = "{}",
            OutlinksJson = "[]",
            ParsedAtUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        seed.SetRenderedHtmlGz([1, 2, 3]);
        await _documentRepository.AddAsync(seed, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _documentRepository.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        return fileId;
    }
}
