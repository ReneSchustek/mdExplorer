using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Data;
using MdExplorer.Data.Repositories;
using MdExplorer.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Indexer.Tests.Integration;

/// <summary>
/// Stellt sicher, dass <see cref="AllFilesQuery"/> die Tag-Slugs korrekt pro Datei aggregiert
/// und nach <c>LastWriteTimeUtc</c> absteigend sortiert.
/// </summary>
public sealed class AllFilesQueryIntegrationTests : IAsyncDisposable
{
    private static readonly Guid FileAId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid FileBId = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TagXId = new("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid TagYId = new("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private readonly SqliteConnection _connection;
    private readonly MdExplorerDbContext _dbContext;

    public AllFilesQueryIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<MdExplorerDbContext> options = new DbContextOptionsBuilder<MdExplorerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

        _dbContext = new MdExplorerDbContext(options);
        _ = _dbContext.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task GetAllAsync_LoadsFilesWithTagsOrderedByLastWriteDesc()
    {
        await SeedAsync().ConfigureAwait(true);
        AllFilesQuery sut = new(_dbContext);

        IReadOnlyList<AllFilesRow> rows = await sut.GetAllAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, rows.Count);
        Assert.Equal(FileBId, rows[0].MarkdownFileId);
        Assert.Equal(FileAId, rows[1].MarkdownFileId);
        Assert.Equal(2, rows[1].TagSlugs.Count);
        Assert.Contains("x", rows[1].TagSlugs);
        Assert.Contains("y", rows[1].TagSlugs);
        Assert.Empty(rows[0].TagSlugs);
    }

    [Fact]
    public async Task GetAllAsync_OnEmptyDatabase_ReturnsEmpty()
    {
        AllFilesQuery sut = new(_dbContext);

        IReadOnlyList<AllFilesRow> rows = await sut.GetAllAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(rows);
    }

    private async Task SeedAsync()
    {
        DateTime older = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
        DateTime newer = new(2026, 6, 9, 9, 0, 0, DateTimeKind.Utc);

        _ = _dbContext.Set<MarkdownFile>().Add(new MarkdownFile
        {
            Id = FileAId,
            AbsolutePath = @"C:\notes\Alpha.md",
            RelativePath = "Alpha.md",
            FileNameWithoutExtension = "Alpha",
            ContentHash = "h-a",
            LastWriteTimeUtc = older,
        });
        _ = _dbContext.Set<MarkdownFile>().Add(new MarkdownFile
        {
            Id = FileBId,
            AbsolutePath = @"C:\notes\Beta.md",
            RelativePath = "Beta.md",
            FileNameWithoutExtension = "Beta",
            ContentHash = "h-b",
            LastWriteTimeUtc = newer,
        });
        _ = _dbContext.Set<Tag>().Add(new Tag { Id = TagXId, Name = "X", Slug = "x" });
        _ = _dbContext.Set<Tag>().Add(new Tag { Id = TagYId, Name = "Y", Slug = "y" });
        _ = _dbContext.Set<MarkdownFileTag>().Add(new MarkdownFileTag { MarkdownFileId = FileAId, TagId = TagXId });
        _ = _dbContext.Set<MarkdownFileTag>().Add(new MarkdownFileTag { MarkdownFileId = FileAId, TagId = TagYId });
        _ = await _dbContext.SaveChangesAsync().ConfigureAwait(false);
    }
}
