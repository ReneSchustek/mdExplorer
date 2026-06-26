using MdExplorer.Core.Abstractions;
using MdExplorer.Graph.Abstractions;
using MdExplorer.Graph.Models;
using MdExplorer.Graph.Options;
using MdExplorer.Graph.Services;
using MdExplorer.Parser.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.Graph.Tests.Services;

public sealed class GraphServiceTests
{
    private static readonly Guid IndexId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AlphaId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BetaId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task BuildSnapshotAsync_OnEmptySource_ReturnsEmptySnapshot()
    {
        FakeSourceProvider provider = new(new GraphSourceData([], []));
        GraphService sut = Build(provider);

        GraphSnapshot snapshot = await sut.BuildSnapshotAsync(GraphFilter.None, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(snapshot.Nodes);
        Assert.Empty(snapshot.Edges);
    }

    [Fact]
    public async Task BuildSnapshotAsync_OnDocumentsWithLinks_BuildsCorrectEdgesAndIncomingCount()
    {
        FakeSourceProvider provider = new(new GraphSourceData(
            [
                new GraphSourceFile(IndexId, "Index", "Index.md"),
                new GraphSourceFile(AlphaId, "Alpha", "Alpha.md"),
                new GraphSourceFile(BetaId, "Beta", "Beta.md"),
            ],
            [
                new GraphSourceDocument(IndexId, """["alpha","beta"]"""),
                new GraphSourceDocument(AlphaId, """["beta"]"""),
                new GraphSourceDocument(BetaId, """[]"""),
            ]));
        GraphService sut = Build(provider);

        GraphSnapshot snapshot = await sut.BuildSnapshotAsync(GraphFilter.None, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(3, snapshot.Nodes.Count);
        Assert.Equal(3, snapshot.Edges.Count);
        GraphNode beta = snapshot.Nodes.Single(n => n.Id == BetaId);
        Assert.Equal(2, beta.IncomingLinkCount);
        GraphNode alpha = snapshot.Nodes.Single(n => n.Id == AlphaId);
        Assert.Equal(1, alpha.IncomingLinkCount);
        GraphNode index = snapshot.Nodes.Single(n => n.Id == IndexId);
        Assert.Equal(0, index.IncomingLinkCount);
    }

    [Fact]
    public async Task BuildSnapshotAsync_OnBrokenLink_ExcludesEdge()
    {
        FakeSourceProvider provider = new(new GraphSourceData(
            [new GraphSourceFile(IndexId, "Index", "Index.md")],
            [new GraphSourceDocument(IndexId, """["does-not-exist"]""")]));
        GraphService sut = Build(provider);

        GraphSnapshot snapshot = await sut.BuildSnapshotAsync(GraphFilter.None, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(snapshot.Edges);
    }

    [Fact]
    public async Task BuildSnapshotAsync_OnSelfLink_ExcludesEdge()
    {
        FakeSourceProvider provider = new(new GraphSourceData(
            [new GraphSourceFile(IndexId, "Index", "Index.md")],
            [new GraphSourceDocument(IndexId, """["index"]""")]));
        GraphService sut = Build(provider);

        GraphSnapshot snapshot = await sut.BuildSnapshotAsync(GraphFilter.None, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(snapshot.Edges);
    }

    [Fact]
    public async Task BuildSnapshotAsync_OnInvalidJson_DoesNotThrow()
    {
        FakeSourceProvider provider = new(new GraphSourceData(
            [new GraphSourceFile(IndexId, "Index", "Index.md")],
            [new GraphSourceDocument(IndexId, "{ broken json")]));
        GraphService sut = Build(provider);

        GraphSnapshot snapshot = await sut.BuildSnapshotAsync(GraphFilter.None, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(snapshot.Edges);
        _ = Assert.Single(snapshot.Nodes);
    }

    [Fact]
    public void JsonBuilder_Serialize_ProducesNodesAndEdgesProperties()
    {
        GraphSnapshot snapshot = new(
            [new GraphNode(IndexId, "Index", "Index.md", 0), new GraphNode(AlphaId, "Alpha", "Alpha.md", 1)],
            [new GraphEdge(IndexId, AlphaId)]);

        string json = GraphJsonBuilder.Serialize(snapshot);

        AssertContains(json, "\"nodes\"");
        AssertContains(json, "\"edges\"");
        AssertContains(json, "\"incomingLinkCount\":1");
        AssertContains(json, "\"sourceId\"");
    }

    [Fact]
    public async Task BuildSnapshotAsync_WithDefaultFilter_DropsIsolatedNodes()
    {
        FakeSourceProvider provider = new(new GraphSourceData(
            [
                new GraphSourceFile(IndexId, "Index", "Index.md"),
                new GraphSourceFile(AlphaId, "Alpha", "Alpha.md"),
                new GraphSourceFile(BetaId, "Beta", "Beta.md"),
            ],
            [
                new GraphSourceDocument(IndexId, """["alpha"]"""),
                new GraphSourceDocument(AlphaId, """[]"""),
                new GraphSourceDocument(BetaId, """[]"""),
            ]));
        GraphService sut = Build(provider, new GraphOptions { IncludeIsolatedNodes = false });

        GraphSnapshot snapshot = await sut.BuildSnapshotAsync(GraphFilter.None, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, snapshot.Nodes.Count);
        Assert.Contains(snapshot.Nodes, n => n.Id == IndexId);
        Assert.Contains(snapshot.Nodes, n => n.Id == AlphaId);
        Assert.DoesNotContain(snapshot.Nodes, n => n.Id == BetaId);
        Assert.Equal(3, snapshot.OriginalNodeCount);
        Assert.Equal(1, snapshot.OriginalEdgeCount);
    }

    [Fact]
    public async Task BuildSnapshotAsync_WithMaxNodes_RetainsTopByDegree()
    {
        Guid hubId = new("44444444-4444-4444-4444-444444444444");
        Guid leafA = new("55555555-5555-5555-5555-555555555555");
        Guid leafB = new("66666666-6666-6666-6666-666666666666");
        FakeSourceProvider provider = new(new GraphSourceData(
            [
                new GraphSourceFile(hubId, "Hub", "Hub.md"),
                new GraphSourceFile(leafA, "LeafA", "LeafA.md"),
                new GraphSourceFile(leafB, "LeafB", "LeafB.md"),
            ],
            [
                new GraphSourceDocument(hubId, """["leafa","leafb"]"""),
                new GraphSourceDocument(leafA, """["hub"]"""),
                new GraphSourceDocument(leafB, """["hub"]"""),
            ]));
        GraphService sut = Build(provider, new GraphOptions { IncludeIsolatedNodes = true, MaxNodes = 2 });

        GraphSnapshot snapshot = await sut.BuildSnapshotAsync(GraphFilter.None, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, snapshot.Nodes.Count);
        Assert.Contains(snapshot.Nodes, n => n.Id == hubId);
        // Hub hat den hoechsten Verbindungsgrad (in 2 + out 2); ein Leaf reicht (in 1 + out 1).
        _ = Assert.Single(snapshot.Nodes, n => n.Id == leafA || n.Id == leafB);
        Assert.Equal(3, snapshot.OriginalNodeCount);
        Assert.Equal(4, snapshot.OriginalEdgeCount);
    }

    [Fact]
    public async Task BuildSnapshotAsync_WithPathExclusion_FiltersMatchingPaths()
    {
        FakeSourceProvider provider = new(new GraphSourceData(
            [
                new GraphSourceFile(IndexId, "Index", "Index.md"),
                new GraphSourceFile(AlphaId, "Alpha", "vendor/Alpha.md"),
                new GraphSourceFile(BetaId, "Beta", "Beta.md"),
            ],
            [
                new GraphSourceDocument(IndexId, """["alpha","beta"]"""),
                new GraphSourceDocument(AlphaId, """[]"""),
                new GraphSourceDocument(BetaId, """[]"""),
            ]));
        GraphOptions options = new() { IncludeIsolatedNodes = true };
        options.PathExclusions.Clear();
        options.PathExclusions.Add("vendor/**");
        GraphService sut = Build(provider, options);

        GraphSnapshot snapshot = await sut.BuildSnapshotAsync(GraphFilter.None, CancellationToken.None).ConfigureAwait(true);

        Assert.DoesNotContain(snapshot.Nodes, n => n.Id == AlphaId);
        Assert.Contains(snapshot.Nodes, n => n.Id == IndexId);
        Assert.Contains(snapshot.Nodes, n => n.Id == BetaId);
        // Kante Index->Alpha wird mit dem Knoten verworfen; Index->Beta bleibt.
        _ = Assert.Single(snapshot.Edges);
        Assert.Equal(3, snapshot.OriginalNodeCount);
        Assert.Equal(2, snapshot.OriginalEdgeCount);
    }

    [Fact]
    public async Task BuildSnapshotAsync_WithPathPrefix_KeepsOnlyMatchingFiles()
    {
        Guid briefId = new("77777777-7777-7777-7777-777777777777");
        FakeSourceProvider provider = new(new GraphSourceData(
            [
                new GraphSourceFile(IndexId, "Index", "Index.md"),
                new GraphSourceFile(briefId, "Brief", "briefs/Brief.md"),
            ],
            [
                new GraphSourceDocument(IndexId, """["brief"]"""),
                new GraphSourceDocument(briefId, """[]"""),
            ]));
        GraphService sut = Build(provider, new GraphOptions { IncludeIsolatedNodes = true });

        GraphSnapshot snapshot = await sut.BuildSnapshotAsync(new GraphFilter("briefs/"), CancellationToken.None).ConfigureAwait(true);

        _ = Assert.Single(snapshot.Nodes);
        Assert.Equal(briefId, snapshot.Nodes[0].Id);
        Assert.Empty(snapshot.Edges);
        Assert.Equal(2, snapshot.OriginalNodeCount);
        Assert.Equal(1, snapshot.OriginalEdgeCount);
    }

    private static void AssertContains(string json, string fragment) =>
        Assert.Contains(fragment, json, StringComparison.Ordinal);

    private static GraphService Build(IGraphSourceProvider provider) =>
        Build(provider, new GraphOptions { IncludeIsolatedNodes = true });

    private static GraphService Build(IGraphSourceProvider provider, GraphOptions options) =>
        new(provider, new TagNormalizer(), Microsoft.Extensions.Options.Options.Create(options), NullLogger<GraphService>.Instance);

    private sealed class FakeSourceProvider(GraphSourceData data) : IGraphSourceProvider
    {
        public Task<GraphSourceData> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(data);
    }
}
