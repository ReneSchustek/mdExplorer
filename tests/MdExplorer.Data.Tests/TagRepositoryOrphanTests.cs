using MdExplorer.Core.Models;
using MdExplorer.Data.Repositories;
using MdExplorer.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Tests;

/// <summary>
/// Prüft das Wegräumen von Schlagworten, an denen keine Datei mehr hängt.
/// </summary>
/// <remarks>
/// Ein Schlagwort ist abgeleitet — es entsteht, weil eine Datei es nennt. Nennt es keine
/// mehr, blieb die Zeile bisher für immer stehen: Die Auswertung für die Wolke verbindet
/// über die Zuordnungen und lässt sie weg, also fiel sie niemandem auf. Sichtbar wurde das
/// erst an 45 Farbwerten, die vor der heutigen Regel als Schlagwort in den Index geraten
/// waren.
/// </remarks>
public sealed class TagRepositoryOrphanTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MdExplorerDbContext _dbContext;
    private readonly TagRepository _sut;

    public TagRepositoryOrphanTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<MdExplorerDbContext> options = new DbContextOptionsBuilder<MdExplorerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

        _dbContext = new MdExplorerDbContext(options);
        _ = _dbContext.Database.EnsureCreated();
        _sut = new TagRepository(_dbContext);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task RemoveOrphanedTagsAsync_RemovesOnlyTagsWithoutFile()
    {
        Guid fileId = await SeedFileAsync("notiz.md").ConfigureAwait(true);
        Guid gebunden = await SeedTagAsync("sicherheit").ConfigureAwait(true);
        Guid frei = await SeedTagAsync("f59e0b").ConfigureAwait(true);
        await LinkAsync(fileId, gebunden).ConfigureAwait(true);

        int removed = await _sut.RemoveOrphanedTagsAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(1, removed);
        List<string> remaining = await _dbContext.Set<Tag>()
            .AsNoTracking()
            .Select(tag => tag.Slug)
            .ToListAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        Assert.Equal(["sicherheit"], remaining);
        Assert.False(await _dbContext.Set<Tag>().AnyAsync(tag => tag.Id == frei, TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Fact]
    public async Task RemoveOrphanedTagsAsync_OnNothingOrphaned_RemovesNothing()
    {
        Guid fileId = await SeedFileAsync("notiz.md").ConfigureAwait(true);
        Guid tagId = await SeedTagAsync("sicherheit").ConfigureAwait(true);
        await LinkAsync(fileId, tagId).ConfigureAwait(true);

        int removed = await _sut.RemoveOrphanedTagsAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(0, removed);
        Assert.Equal(1, await _dbContext.Set<Tag>().CountAsync(TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    [Fact]
    public async Task RemoveOrphanedTagsAsync_AfterLastFileLost_RemovesTheTag()
    {
        // Der Fall, um den es wirklich geht: Das Schlagwort war einmal richtig verknüpft.
        // Erst als die Datei es nicht mehr nennt, wird die Zeile zu Datenmüll.
        Guid fileId = await SeedFileAsync("palettes.md").ConfigureAwait(true);
        Guid tagId = await SeedTagAsync("f59e0b").ConfigureAwait(true);
        await LinkAsync(fileId, tagId).ConfigureAwait(true);

        await _sut.ReplaceFileTagsAsync(fileId, [], TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _sut.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        int removed = await _sut.RemoveOrphanedTagsAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(1, removed);
        Assert.Equal(0, await _dbContext.Set<Tag>().CountAsync(TestContext.Current.CancellationToken).ConfigureAwait(true));
    }

    private async Task<Guid> SeedFileAsync(string name)
    {
        MarkdownFile file = new()
        {
            Id = Guid.NewGuid(),
            AbsolutePath = @"C:\notes\" + name,
            RelativePath = name,
            FileNameWithoutExtension = Path.GetFileNameWithoutExtension(name),
            ContentHash = "hash-" + name,
            SizeBytes = 1,
            LastWriteTimeUtc = DateTime.UnixEpoch,
            IndexedAtUtc = DateTime.UnixEpoch,
        };

        _ = await _dbContext.Set<MarkdownFile>().AddAsync(file, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        return file.Id;
    }

    private async Task<Guid> SeedTagAsync(string slug)
    {
        Tag tag = new() { Id = Guid.NewGuid(), Slug = slug, Name = slug };
        _ = await _dbContext.Set<Tag>().AddAsync(tag, TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        return tag.Id;
    }

    private async Task LinkAsync(Guid fileId, Guid tagId)
    {
        _ = await _dbContext.Set<MarkdownFileTag>()
            .AddAsync(new MarkdownFileTag { MarkdownFileId = fileId, TagId = tagId }, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
        _ = await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
    }
}
