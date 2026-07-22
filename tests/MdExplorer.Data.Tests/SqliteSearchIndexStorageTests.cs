using MdExplorer.Core.Abstractions;
using MdExplorer.Data;
using MdExplorer.Data.Repositories;
using MdExplorer.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Tests;

/// <summary>
/// Integrationstests fuer <see cref="SqliteSearchIndexStorage"/> gegen ein echtes FTS5-Schema
/// (per Migration erzeugt). Fokus: die gechunkte <see cref="SqliteSearchIndexStorage.LoadBodiesAsync"/>
/// liefert korrekte Bodies fuer beliebige Id-Mengen inklusive nicht existenter Ids.
/// </summary>
public sealed class SqliteSearchIndexStorageTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MdExplorerDbContext _dbContext;
    private readonly SqliteSearchIndexStorage _sut;

    public SqliteSearchIndexStorageTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<MdExplorerDbContext> options = new DbContextOptionsBuilder<MdExplorerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

        _dbContext = new MdExplorerDbContext(options);
        // Volle Migration statt EnsureCreated — die FTS5-Virtual-Table entsteht erst per Sql-Migration.
        _dbContext.Database.Migrate();
        _sut = new SqliteSearchIndexStorage(_dbContext);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task LoadBodiesAsync_OnMultipleIdsIncludingUnknown_ReturnsBodyMapInOneBatch()
    {
        Guid first = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid second = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Guid missing = Guid.Parse("99999999-9999-9999-9999-999999999999");
        await SeedAsync(first, "Erster Body-Text").ConfigureAwait(true);
        await SeedAsync(second, "Zweiter Body-Text").ConfigureAwait(true);

        IReadOnlyDictionary<Guid, string> bodies = await _sut
            .LoadBodiesAsync([first, second, missing], CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(2, bodies.Count);
        Assert.Equal("Erster Body-Text", bodies[first]);
        Assert.Equal("Zweiter Body-Text", bodies[second]);
        Assert.False(bodies.ContainsKey(missing));
    }

    [Fact]
    public async Task LoadBodiesAsync_OnEmptyInput_ReturnsEmptyMap()
    {
        IReadOnlyDictionary<Guid, string> bodies = await _sut
            .LoadBodiesAsync([], CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Empty(bodies);
    }

    [Fact]
    public async Task LoadBodiesAsync_OnMoreThanOneChunk_ReturnsAllBodies()
    {
        // 1.200 > SqliteInListBatchSize (500): erzwingt drei Chunks und deckt die Chunk-Grenze ab.
        const int Count = 1_200;
        List<Guid> ids = new(Count);
        for (int i = 0; i < Count; i++)
        {
            Guid id = Guid.NewGuid();
            ids.Add(id);
            await SeedAsync(id, $"Body-{i}").ConfigureAwait(true);
        }

        IReadOnlyDictionary<Guid, string> bodies = await _sut
            .LoadBodiesAsync(ids, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(Count, bodies.Count);
        Assert.Equal("Body-0", bodies[ids[0]]);
        Assert.Equal($"Body-{Count - 1}", bodies[ids[Count - 1]]);
    }

    private Task SeedAsync(Guid markdownFileId, string body)
    {
        SearchIndexEntry entry = new(
            MarkdownFileId: markdownFileId,
            Title: "Titel",
            Body: body,
            Tags: string.Empty,
            Frontmatter: string.Empty,
            Path: "notes/datei.md",
            SourceContentHash: "hash");
        return _sut.ApplyChangesAsync([], [entry], CancellationToken.None);
    }
}
