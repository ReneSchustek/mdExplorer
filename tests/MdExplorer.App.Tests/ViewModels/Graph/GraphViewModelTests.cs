using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels.Graph;
using MdExplorer.Graph.Abstractions;
using MdExplorer.Graph.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.ViewModels.Graph;

/// <summary>
/// Das Graph-Fenster lädt den Snapshot mit dem gewählten Pfadfilter, übersteht einen Fehler des
/// Dienstes ohne Ausnahme und ohne hängende Belegt-Anzeige und merkt sich den Filter, mit dem
/// zuletzt geladen wurde, über einen Neustart hinweg.
/// </summary>
public sealed class GraphViewModelTests : IDisposable
{
    private static readonly Guid NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly ServiceCollection _services = [];
    private ServiceProvider? _provider;

    /// <inheritdoc />
    public void Dispose() => _provider?.Dispose();

    [Fact]
    public async Task RefreshAsync_OnSnapshot_PopulatesCountsAndJson()
    {
        FakeGraphService graph = new()
        {
            Snapshot = new GraphSnapshot(
                [new GraphNode(NodeId, "Titel", "titel.md", 2)],
                [],
                OriginalNodeCount: 5,
                OriginalEdgeCount: 3),
        };
        GraphViewModel sut = BuildViewModel(graph, new FakeUiLayoutStore());

        await sut.RefreshAsync().ConfigureAwait(true);

        Assert.Equal(1, sut.NodeCount);
        Assert.Equal(0, sut.EdgeCount);
        Assert.Equal(5, sut.OriginalNodeCount);
        Assert.Equal(3, sut.OriginalEdgeCount);
        Assert.NotNull(sut.SnapshotJson);
        Assert.Contains("nodes", sut.SnapshotJson, StringComparison.Ordinal);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task RefreshAsync_WithPathPrefix_PassesFilterToService()
    {
        FakeGraphService graph = new();
        GraphViewModel sut = BuildViewModel(graph, new FakeUiLayoutStore());
        sut.PathPrefix = "notizen/";

        await sut.RefreshAsync().ConfigureAwait(true);

        Assert.Equal("notizen/", graph.LastFilter?.PathPrefix);
    }

    [Theory]
    [InlineData(TestFailures.Database)]
    [InlineData(TestFailures.InvalidState)]
    public async Task RefreshAsync_OnServiceFailure_DoesNotThrowAndResetsBusy(string kind)
    {
        FakeGraphService graph = new() { Failure = TestFailures.Of(kind) };
        GraphViewModel sut = BuildViewModel(graph, new FakeUiLayoutStore());

        await sut.RefreshAsync().ConfigureAwait(true);

        Assert.False(sut.IsBusy);
        Assert.Null(sut.SnapshotJson);
    }

    [Fact]
    public async Task RefreshAsync_WhenTheApplicationShutsDown_EndsQuietly()
    {
        // EF Core meldet den Abbruch als OperationCanceledException, nicht als deren Unterart
        // TaskCanceledException; ein Fang nur für die Unterart ließ ihn aus dem Befehl fliegen.
        FakeGraphService graph = new() { Failure = new OperationCanceledException() };
        GraphViewModel sut = BuildViewModel(graph, new FakeUiLayoutStore());

        await sut.RefreshAsync().ConfigureAwait(true);

        Assert.False(sut.IsBusy);
    }

    [Fact]
    public void TypingThePrefix_DoesNotWriteTheLayoutOnEveryKeystroke()
    {
        FakeUiLayoutStore store = new();
        GraphViewModel sut = BuildViewModel(new FakeGraphService(), store);

        sut.PathPrefix = "d";
        sut.PathPrefix = "do";
        sut.PathPrefix = "docs/";

        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task RefreshAsync_RemembersThePrefixItLoadedWith()
    {
        FakeUiLayoutStore store = new();
        GraphViewModel sut = BuildViewModel(new FakeGraphService(), store);
        sut.PathPrefix = "docs/";

        await sut.RefreshAsync().ConfigureAwait(true);

        Assert.Equal("docs/", store.Load().GraphPathPrefix);
    }

    [Fact]
    public async Task RefreshAsync_WithAWhitespacePrefix_RemembersNoPrefix()
    {
        FakeUiLayoutStore store = new() { Layout = UiLayout.Default with { GraphPathPrefix = "docs/" } };
        GraphViewModel sut = BuildViewModel(new FakeGraphService(), store);
        sut.PathPrefix = "   ";

        await sut.RefreshAsync().ConfigureAwait(true);

        Assert.Null(store.Load().GraphPathPrefix);
    }

    [Fact]
    public async Task RefreshAsync_WithTheRememberedPrefix_WritesNothing()
    {
        FakeUiLayoutStore store = new() { Layout = UiLayout.Default with { GraphPathPrefix = "docs/" } };
        GraphViewModel sut = BuildViewModel(new FakeGraphService(), store);

        await sut.RefreshAsync().ConfigureAwait(true);

        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void Constructor_LoadsPersistedPrefix()
    {
        FakeUiLayoutStore store = new() { Layout = UiLayout.Default with { GraphPathPrefix = "vorher/" } };

        GraphViewModel sut = BuildViewModel(new FakeGraphService(), store);

        Assert.Equal("vorher/", sut.PathPrefix);
        Assert.Equal(0, store.SaveCount);
    }

    private GraphViewModel BuildViewModel(FakeGraphService graph, FakeUiLayoutStore store)
    {
        _ = _services.AddScoped<IGraphService>(_ => graph);
        _provider = _services.BuildServiceProvider();
        return new GraphViewModel(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            store,
            NullLogger<GraphViewModel>.Instance);
    }

    /// <summary>Liefert einen vorgegebenen Snapshot oder Fehler und merkt sich den Filter.</summary>
    private sealed class FakeGraphService : IGraphService
    {
        public GraphSnapshot Snapshot { get; init; } = GraphSnapshot.Empty;

        public Exception? Failure { get; init; }

        public GraphFilter? LastFilter { get; private set; }

        public Task<GraphSnapshot> BuildSnapshotAsync(GraphFilter filter, CancellationToken cancellationToken)
        {
            LastFilter = filter;
            return Failure is not null
                ? Task.FromException<GraphSnapshot>(Failure)
                : Task.FromResult(Snapshot);
        }

        /// <summary>Für diese Tests ohne Belang, denn das Bild kennt keine Einzelverbindungen.</summary>
        public Task<DocumentRelations> GetRelationsAsync(Guid markdownFileId, CancellationToken cancellationToken) =>
            Task.FromResult(DocumentRelations.Empty);
    }
}
