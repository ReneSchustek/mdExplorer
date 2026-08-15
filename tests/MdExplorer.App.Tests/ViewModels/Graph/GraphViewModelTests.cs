using System.IO;
using MdExplorer.App.Services;
using MdExplorer.App.ViewModels.Graph;
using MdExplorer.Graph.Abstractions;
using MdExplorer.Graph.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.ViewModels.Graph;

/// <summary>Tests für das Graph-Panel-ViewModel — Snapshot-Laden und Pfad-Prefix-Persistenz.</summary>
public sealed class GraphViewModelTests : IDisposable
{
    private static readonly Guid NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly string _layoutPath = Path.Combine(Path.GetTempPath(), $"mdex-uilayout-{Guid.NewGuid():N}.json");

    /// <inheritdoc />
    public void Dispose()
    {
        if (File.Exists(_layoutPath))
        {
            File.Delete(_layoutPath);
        }
    }

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
        GraphViewModel sut = BuildViewModel(graph, out UiSettingsStore _);

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
        GraphViewModel sut = BuildViewModel(graph, out UiSettingsStore _);
        sut.PathPrefix = "notizen/";

        await sut.RefreshAsync().ConfigureAwait(true);

        Assert.Equal("notizen/", graph.LastFilter?.PathPrefix);
    }

    [Fact]
    public async Task RefreshAsync_OnServiceFailure_DoesNotThrowAndResetsBusy()
    {
        FakeGraphService graph = new() { Failure = new InvalidOperationException("scope broken") };
        GraphViewModel sut = BuildViewModel(graph, out UiSettingsStore _);

        await sut.RefreshAsync().ConfigureAwait(true);

        Assert.False(sut.IsBusy);
        Assert.Null(sut.SnapshotJson);
    }

    [Fact]
    public void OnPathPrefixChanged_PersistsPrefixToStore()
    {
        GraphViewModel sut = BuildViewModel(new FakeGraphService(), out UiSettingsStore store);

        sut.PathPrefix = "docs/";

        Assert.Equal("docs/", store.Load().GraphPathPrefix);
    }

    [Fact]
    public void OnPathPrefixChanged_OnWhitespace_PersistsNull()
    {
        GraphViewModel sut = BuildViewModel(new FakeGraphService(), out UiSettingsStore store);
        sut.PathPrefix = "docs/";

        sut.PathPrefix = "   ";

        Assert.Null(store.Load().GraphPathPrefix);
    }

    [Fact]
    public void Constructor_LoadsPersistedPrefix()
    {
        UiSettingsStore store = new(_layoutPath, NullLogger<UiSettingsStore>.Instance);
        store.Save(UiLayout.Default with { GraphPathPrefix = "vorher/" });

        GraphViewModel sut = BuildViewModel(new FakeGraphService(), store);

        Assert.Equal("vorher/", sut.PathPrefix);
    }

    private GraphViewModel BuildViewModel(FakeGraphService graph, out UiSettingsStore store)
    {
        store = new UiSettingsStore(_layoutPath, NullLogger<UiSettingsStore>.Instance);
        return BuildViewModel(graph, store);
    }

    private static GraphViewModel BuildViewModel(FakeGraphService graph, UiSettingsStore store)
    {
        ServiceCollection services = [];
        _ = services.AddScoped<IGraphService>(_ => graph);
        ServiceProvider provider = services.BuildServiceProvider();
        return new GraphViewModel(
            provider.GetRequiredService<IServiceScopeFactory>(),
            store,
            NullLogger<GraphViewModel>.Instance);
    }

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

        /// <summary>Für diese Tests ohne Belang — das Bild kennt keine Einzelverbindungen.</summary>
        public Task<DocumentRelations> GetRelationsAsync(Guid markdownFileId, CancellationToken cancellationToken) =>
            Task.FromResult(DocumentRelations.Empty);
    }
}
