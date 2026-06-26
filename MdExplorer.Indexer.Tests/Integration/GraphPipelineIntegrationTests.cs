using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Data;
using MdExplorer.Data.Repositories;
using MdExplorer.Data.Sqlite;
using MdExplorer.Graph.Models;
using MdExplorer.Graph.Services;
using MdExplorer.Parser.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.Indexer.Tests.Integration;

/// <summary>
/// Integrationstest der Graph-Pipeline: leere DB → indizierte Files mit verlinkten WikiLinks
/// → <see cref="GraphService.BuildSnapshotAsync"/> liefert die erwarteten Knoten und Kanten.
/// Stellt sicher, dass die echte EF-Pipeline (`GraphSourceProvider`) und das Parser-Output-Format
/// (Slug-Normalisierung im <c>OutlinksJson</c>) zueinander passen.
/// </summary>
public sealed class GraphPipelineIntegrationTests : IAsyncDisposable
{
    private static readonly Guid AlphaId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BetaId = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime FixedUtc = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly MdExplorerDbContext _dbContext;

    public GraphPipelineIntegrationTests()
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
    public async Task BuildSnapshotAsync_OnTwoFilesWithMutualWikiLinks_ProducesNodesAndEdges()
    {
        await SeedAsync(AlphaId, "Alpha", "Alpha.md", outlinkSlugs: "beta").ConfigureAwait(true);
        await SeedAsync(BetaId, "Beta", "Beta.md", outlinkSlugs: "alpha").ConfigureAwait(true);

        GraphService graphService = new(
            new GraphSourceProvider(_dbContext),
            new TagNormalizer(),
            Microsoft.Extensions.Options.Options.Create(new MdExplorer.Graph.Options.GraphOptions { IncludeIsolatedNodes = true }),
            NullLogger<GraphService>.Instance);

        GraphSnapshot snapshot = await graphService.BuildSnapshotAsync(GraphFilter.None, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, snapshot.Nodes.Count);
        Assert.Equal(2, snapshot.Edges.Count);
        Assert.Contains(snapshot.Edges, edge => edge.SourceId == AlphaId && edge.TargetId == BetaId);
        Assert.Contains(snapshot.Edges, edge => edge.SourceId == BetaId && edge.TargetId == AlphaId);
    }

    [Fact]
    public async Task BuildSnapshotAsync_OnEmptyDatabase_ReturnsEmptySnapshot()
    {
        GraphService graphService = new(
            new GraphSourceProvider(_dbContext),
            new TagNormalizer(),
            Microsoft.Extensions.Options.Options.Create(new MdExplorer.Graph.Options.GraphOptions { IncludeIsolatedNodes = true }),
            NullLogger<GraphService>.Instance);

        GraphSnapshot snapshot = await graphService.BuildSnapshotAsync(GraphFilter.None, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(snapshot.Nodes);
        Assert.Empty(snapshot.Edges);
    }

    private async Task SeedAsync(Guid fileId, string title, string relativePath, string outlinkSlugs)
    {
        MarkdownFile file = new()
        {
            Id = fileId,
            AbsolutePath = $@"C:\notes\{relativePath}",
            RelativePath = relativePath,
            FileNameWithoutExtension = title,
            ContentHash = $"hash-{title}",
        };
        _ = _dbContext.Set<MarkdownFile>().Add(file);

        MarkdownDocument document = new()
        {
            Id = Guid.NewGuid(),
            MarkdownFileId = fileId,
            SourceContentHash = $"hash-{title}",
            FrontmatterJson = "{}",
            OutlinksJson = $"[\"{outlinkSlugs}\"]",
            ParsedAtUtc = FixedUtc,
        };
        document.SetRenderedHtmlGz([1, 2, 3]);
        _ = _dbContext.Set<MarkdownDocument>().Add(document);

        _ = await _dbContext.SaveChangesAsync().ConfigureAwait(false);
    }
}
