using System.IO;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Data;
using MdExplorer.Data.Repositories;
using MdExplorer.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Tests;

/// <summary>Integrationstests für die Read-Query-Klassen gegen eine echte In-Memory-SQLite.</summary>
public sealed class DataQueriesTests : IAsyncDisposable
{
    private static readonly DateTime FixedUtc = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly MdExplorerDbContext _dbContext;

    public DataQueriesTests()
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
    public async Task MarkdownSourceProvider_EnumerateAsync_YieldsAllFilesOrderedById()
    {
        Guid a = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid b = Guid.Parse("22222222-2222-2222-2222-222222222222");
        await SeedFileAsync(a, @"C:\V\a.md", "a.md", "hash-a").ConfigureAwait(true);
        await SeedFileAsync(b, @"C:\V\b.md", "b.md", "hash-b").ConfigureAwait(true);
        MarkdownSourceProvider sut = new(_dbContext);

        List<MarkdownSourceSnapshot> result = [];
        await foreach (MarkdownSourceSnapshot snapshot in sut.EnumerateAsync(CancellationToken.None).ConfigureAwait(true))
        {
            result.Add(snapshot);
        }

        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.Id == a && s.ContentHash == "hash-a");
        Assert.Contains(result, s => s.Id == b && s.AbsolutePath == @"C:\V\b.md");
    }

    [Fact]
    public async Task TagFileLookupQuery_GetFilesByTagSlug_ReturnsLinkedFilesOrderedByRelativePath()
    {
        Guid fileZ = Guid.Parse("33333333-3333-3333-3333-333333333333");
        Guid fileA = Guid.Parse("44444444-4444-4444-4444-444444444444");
        Guid fileOther = Guid.Parse("55555555-5555-5555-5555-555555555555");
        await SeedFileAsync(fileZ, @"C:\V\z.md", "z.md", "h1").ConfigureAwait(true);
        await SeedFileAsync(fileA, @"C:\V\a.md", "a.md", "h2").ConfigureAwait(true);
        await SeedFileAsync(fileOther, @"C:\V\o.md", "o.md", "h3").ConfigureAwait(true);
        Guid tagId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        await SeedTagWithLinksAsync(tagId, "wichtig", [fileZ, fileA]).ConfigureAwait(true);

        TagFileLookupQuery sut = new(_dbContext);
        IReadOnlyList<TagFileLookupRow> rows = await sut.GetFilesByTagSlugAsync("wichtig", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, rows.Count);
        Assert.Equal("a.md", rows[0].RelativePath); // alphabetisch vor z.md
        Assert.Equal("z.md", rows[1].RelativePath);
        Assert.DoesNotContain(rows, r => r.MarkdownFileId == fileOther);
    }

    [Fact]
    public async Task TagFileLookupQuery_GetFilesByTagSlug_OnUnknownSlug_ReturnsEmpty()
    {
        TagFileLookupQuery sut = new(_dbContext);

        IReadOnlyList<TagFileLookupRow> rows = await sut.GetFilesByTagSlugAsync("gibt-es-nicht", CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(rows);
    }

    private async Task SeedFileAsync(Guid id, string absolutePath, string relativePath, string hash)
    {
        MarkdownFile file = new()
        {
            Id = id,
            AbsolutePath = absolutePath,
            RelativePath = relativePath,
            FileNameWithoutExtension = Path.GetFileNameWithoutExtension(relativePath),
            SizeBytes = 0,
            LastWriteTimeUtc = FixedUtc,
            ContentHash = hash,
            IndexedAtUtc = FixedUtc,
        };
        _ = await _dbContext.Set<MarkdownFile>().AddAsync(file).ConfigureAwait(true);
        _ = await _dbContext.SaveChangesAsync().ConfigureAwait(true);
    }

    private async Task SeedTagWithLinksAsync(Guid tagId, string slug, IReadOnlyList<Guid> fileIds)
    {
        Tag tag = new() { Id = tagId, Name = slug, Slug = slug };
        _ = await _dbContext.Set<Tag>().AddAsync(tag).ConfigureAwait(true);
        foreach (Guid fileId in fileIds)
        {
            _ = await _dbContext.Set<MarkdownFileTag>().AddAsync(new MarkdownFileTag { MarkdownFileId = fileId, TagId = tagId }).ConfigureAwait(true);
        }
        _ = await _dbContext.SaveChangesAsync().ConfigureAwait(true);
    }
}
