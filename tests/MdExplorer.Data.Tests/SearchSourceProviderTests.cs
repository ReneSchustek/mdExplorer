using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Data.Repositories;
using MdExplorer.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MdExplorer.Data.Tests;

/// <summary>
/// Der Quellenanbieter des Suchindex liefert alle geparsten Dokumente in fester Reihenfolge und
/// die Schlagwort-Namen je Datei.
/// </summary>
/// <remarks>
/// Die Reihenfolge ist Teil des Vertrags: Der Abgleich des Suchindex schneidet nach einer
/// Stapelgröße ab und soll bei jedem Takt denselben Stapel wählen.
/// </remarks>
public sealed class SearchSourceProviderTests : IAsyncDisposable
{
    private static readonly DateTime FixedUtc = new(2026, 8, 16, 9, 0, 0, DateTimeKind.Utc);

    private readonly TestIdSequence _ids = new();
    private readonly SqliteConnection _connection;
    private readonly MdExplorerDbContext _dbContext;
    private readonly SearchSourceProvider _sut;

    public SearchSourceProviderTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        DbContextOptions<MdExplorerDbContext> options = new DbContextOptionsBuilder<MdExplorerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;
        _dbContext = new MdExplorerDbContext(options);
        _ = _dbContext.Database.EnsureCreated();
        _sut = new SearchSourceProvider(_dbContext);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task LoadAsync_WithDocumentsAddedOutOfOrder_ReturnsThemOrderedById()
    {
        await SeedDocumentAsync(IdOf(3), "dritte").ConfigureAwait(true);
        await SeedDocumentAsync(IdOf(1), "erste").ConfigureAwait(true);
        await SeedDocumentAsync(IdOf(2), "zweite").ConfigureAwait(true);

        SearchSourceData result = await _sut.LoadAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal([IdOf(1), IdOf(2), IdOf(3)], result.Documents.Select(document => document.MarkdownFileId));
        Assert.Equal("erste", result.Documents[0].Title);
        Assert.Equal("unter/erste.md", result.Documents[0].RelativePath);
    }

    [Fact]
    public async Task LoadAsync_WithTaggedFiles_GroupsTheOriginalNamesPerFile()
    {
        await SeedDocumentAsync(IdOf(1), "erste").ConfigureAwait(true);
        await SeedDocumentAsync(IdOf(2), "zweite").ConfigureAwait(true);
        Guid projekt = await SeedTagAsync("Projekt X", "projekt-x").ConfigureAwait(true);
        Guid idee = await SeedTagAsync("Idee", "idee").ConfigureAwait(true);
        await LinkAsync(IdOf(1), projekt).ConfigureAwait(true);
        await LinkAsync(IdOf(1), idee).ConfigureAwait(true);

        SearchSourceData result = await _sut.LoadAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(["Idee", "Projekt X"], result.TagsByFileId[IdOf(1)].Order(StringComparer.Ordinal));
        Assert.False(result.TagsByFileId.ContainsKey(IdOf(2)));
    }

    [Fact]
    public async Task LoadVersionAsync_AfterANewDocument_ChangesTheMarker()
    {
        SearchSourceVersion empty = await _sut.LoadVersionAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(new SearchSourceVersion(0, null, 0, null), empty);

        await SeedDocumentAsync(IdOf(1), "erste").ConfigureAwait(true);
        SearchSourceVersion first = await _sut.LoadVersionAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        SearchSourceVersion again = await _sut.LoadVersionAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(new SearchSourceVersion(1, FixedUtc, 1, FixedUtc), first);
        Assert.Equal(first, again);
    }

    private static Guid IdOf(int index)
        => new(index.ToString("D8", System.Globalization.CultureInfo.InvariantCulture) + "-0000-0000-0000-000000000000");

    private async Task<Guid> SeedTagAsync(string name, string slug)
    {
        Tag tag = new() { Id = _ids.Next(), Name = name, Slug = slug };
        _ = await _dbContext.Set<Tag>().AddAsync(tag).ConfigureAwait(true);
        _ = await _dbContext.SaveChangesAsync().ConfigureAwait(true);
        return tag.Id;
    }

    private async Task LinkAsync(Guid fileId, Guid tagId)
    {
        _ = await _dbContext.Set<MarkdownFileTag>().AddAsync(new MarkdownFileTag { MarkdownFileId = fileId, TagId = tagId }).ConfigureAwait(true);
        _ = await _dbContext.SaveChangesAsync().ConfigureAwait(true);
    }

    private async Task SeedDocumentAsync(Guid id, string name)
    {
        MarkdownFile file = new()
        {
            Id = id,
            AbsolutePath = @"C:\notes\unter\" + name + ".md",
            RelativePath = "unter/" + name + ".md",
            FileNameWithoutExtension = name,
            SizeBytes = 0,
            LastWriteTimeUtc = FixedUtc,
            ContentHash = "hash-" + name,
            IndexedAtUtc = FixedUtc,
        };
        _ = await _dbContext.Set<MarkdownFile>().AddAsync(file).ConfigureAwait(true);

        MarkdownDocument document = new()
        {
            Id = _ids.Next(),
            MarkdownFileId = id,
            SourceContentHash = "hash-" + name,
            FrontmatterJson = "{}",
            OutlinksJson = "[]",
            ParsedAtUtc = FixedUtc,
        };
        _ = await _dbContext.Set<MarkdownDocument>().AddAsync(document).ConfigureAwait(true);
        _ = await _dbContext.SaveChangesAsync().ConfigureAwait(true);
    }
}
