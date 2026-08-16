using MdExplorer.Core.Abstractions;
using MdExplorer.Graph.Tests.Fakes;
using MdExplorer.Graph.Models;
using MdExplorer.Graph.Options;
using MdExplorer.Graph.Services;
using MdExplorer.Parser.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.Graph.Tests.Services;

/// <summary>
/// Tests für die Verbindungen eines einzelnen Dokuments.
/// </summary>
/// <remarks>
/// Der Rückweg ist der Teil, den man leicht vergisst: Wohin ein Dokument verweist, steht in
/// ihm selbst; wer auf es verweist, steht in allen anderen. Eine Richtung allein ist eine
/// Sackgasse mit Umweg.
/// </remarks>
public sealed class GraphServiceRelationsTests
{
    private static readonly Guid IndexId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AlphaId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BetaId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task GetRelationsAsync_ReportsBothDirections()
    {
        GraphService sut = Build(Sample());

        DocumentRelations relations = await sut.GetRelationsAsync(AlphaId, CancellationToken.None).ConfigureAwait(true);

        RelatedDocument outgoing = Assert.Single(relations.Outgoing);
        Assert.Equal(BetaId, outgoing.MarkdownFileId);
        RelatedDocument incoming = Assert.Single(relations.Incoming);
        Assert.Equal(IndexId, incoming.MarkdownFileId);
    }

    [Fact]
    public async Task GetRelationsAsync_ForADocumentNobodyPointsTo_ReportsNoIncoming()
    {
        GraphService sut = Build(Sample());

        DocumentRelations relations = await sut.GetRelationsAsync(IndexId, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, relations.Outgoing.Count);
        Assert.Empty(relations.Incoming);
    }

    [Fact]
    public async Task GetRelationsAsync_CountsARepeatedLinkOnce()
    {
        // Wer eine Datei dreimal im Text erwähnt, hat sie einmal verknüpft.
        GraphSourceData data = new(
            [
                new GraphSourceFile(IndexId, "Index", "Index.md"),
                new GraphSourceFile(AlphaId, "Alpha", "Alpha.md"),
            ],
            [
                new GraphSourceDocument(IndexId, """["alpha","alpha","alpha"]"""),
                new GraphSourceDocument(AlphaId, """[]"""),
            ]);
        GraphService sut = Build(data);

        DocumentRelations relations = await sut.GetRelationsAsync(IndexId, CancellationToken.None).ConfigureAwait(true);

        _ = Assert.Single(relations.Outgoing);
    }

    [Fact]
    public async Task GetRelationsAsync_IgnoresLinksToUnknownTargets()
    {
        GraphSourceData data = new(
            [new GraphSourceFile(IndexId, "Index", "Index.md")],
            [new GraphSourceDocument(IndexId, """["gibt-es-nicht"]""")]);
        GraphService sut = Build(data);

        DocumentRelations relations = await sut.GetRelationsAsync(IndexId, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(relations.Outgoing);
    }

    [Fact]
    public async Task GetRelationsAsync_IgnoresASelfReference()
    {
        GraphSourceData data = new(
            [new GraphSourceFile(IndexId, "Index", "Index.md")],
            [new GraphSourceDocument(IndexId, """["index"]""")]);
        GraphService sut = Build(data);

        DocumentRelations relations = await sut.GetRelationsAsync(IndexId, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(relations.Outgoing);
        Assert.Empty(relations.Incoming);
    }

    [Fact]
    public async Task GetRelationsAsync_OnEmptyId_ReturnsNothing()
    {
        GraphService sut = Build(Sample());

        DocumentRelations relations = await sut.GetRelationsAsync(Guid.Empty, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(relations.Outgoing);
        Assert.Empty(relations.Incoming);
    }

    [Fact]
    public async Task GetRelationsAsync_IsUnaffectedByTheGraphNodeLimit()
    {
        // Die Obergrenze dient der Darstellbarkeit eines Bildes. Was an einem Dokument hängt,
        // darf sie nicht beschneiden — sonst fehlte ein Weg, ohne dass jemand es merkt.
        GraphService sut = Build(Sample(), new GraphOptions { IncludeIsolatedNodes = true, MaxNodes = 1 });

        DocumentRelations relations = await sut.GetRelationsAsync(IndexId, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, relations.Outgoing.Count);
    }

    [Fact]
    public async Task GetRelationsAsync_SortsByPath_SoTheOrderIsStable()
    {
        GraphSourceData data = new(
            [
                new GraphSourceFile(IndexId, "Index", "Index.md"),
                new GraphSourceFile(AlphaId, "Alpha", "z/Alpha.md"),
                new GraphSourceFile(BetaId, "Beta", "a/Beta.md"),
            ],
            [new GraphSourceDocument(IndexId, """["alpha","beta"]""")]);
        GraphService sut = Build(data);

        DocumentRelations relations = await sut.GetRelationsAsync(IndexId, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("a/Beta.md", relations.Outgoing[0].RelativePath);
        Assert.Equal("z/Alpha.md", relations.Outgoing[1].RelativePath);
    }

    private static GraphSourceData Sample() => new(
        [
            new GraphSourceFile(IndexId, "Index", "Index.md"),
            new GraphSourceFile(AlphaId, "Alpha", "Alpha.md"),
            new GraphSourceFile(BetaId, "Beta", "Beta.md"),
        ],
        [
            new GraphSourceDocument(IndexId, """["alpha","beta"]"""),
            new GraphSourceDocument(AlphaId, """["beta"]"""),
            new GraphSourceDocument(BetaId, """[]"""),
        ]);

    private static GraphService Build(GraphSourceData data) =>
        Build(data, new GraphOptions { IncludeIsolatedNodes = true });

    private static GraphService Build(GraphSourceData data, GraphOptions options) =>
        new(
            new FakeGraphSourceProvider(data),
            new TagNormalizer(),
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<GraphService>.Instance);


}
