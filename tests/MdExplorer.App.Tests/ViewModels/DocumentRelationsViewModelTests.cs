using System.IO;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.App.Services;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Graph.Abstractions;
using MdExplorer.Graph.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Der Zusammenhangs-Bereich am geöffneten Dokument zeigt Links in beide Richtungen, Ordner
/// und Schlagwörter und nennt vor dem Löschen oder Umbenennen, wie viele Verweise brechen.
/// </summary>
/// <remarks>
/// Geprüft wird vor allem, dass jeder Eintrag ein Weg ist: Ein Klick meldet, wohin es gehen
/// soll. Ohne das wäre der Bereich eine Aufzählung, nach der man doch wieder sucht.
/// </remarks>
public sealed class DocumentRelationsViewModelTests
{
    private static readonly Guid OpenId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TargetId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SourceId = new("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void BeforeAnyDocument_TheAreaStaysHidden()
    {
        using ServiceProvider provider = BuildProvider(new FakeGraphService(DocumentRelations.Empty), new FakeMarkdownFileRepository());
        DocumentRelationsViewModel sut = Create(provider);

        Assert.True(sut.ShowsNoDocument);
        Assert.False(sut.HasDocument);
    }

    [Fact]
    public async Task LoadAsync_ShowsBothDirectionsFolderAndTags()
    {
        DocumentRelations relations = new(
            [new RelatedDocument(TargetId, "Ziel", "unter/Ziel.md")],
            [new RelatedDocument(SourceId, "Quelle", "Quelle.md")]);
        FakeMarkdownFileRepository files = new();
        files.Add(FileWith(OpenId, "notizen/2026/Offen.md"));
        using ServiceProvider provider = BuildProvider(new FakeGraphService(relations), files);
        DocumentRelationsViewModel sut = Create(provider);

        await sut.LoadAsync(OpenId, ["projekt", "wichtig"], TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(sut.ShowsRelations);
        Assert.Equal("Ziel", Assert.Single(sut.Outgoing).Title);
        Assert.Equal("Quelle", Assert.Single(sut.Incoming).Title);
        Assert.Equal("notizen/2026", sut.FolderPath);
        Assert.Equal(["projekt", "wichtig"], sut.Tags);
    }

    [Fact]
    public async Task LoadAsync_ForAFileInTheRoot_LeavesTheFolderEmpty()
    {
        FakeMarkdownFileRepository files = new();
        files.Add(FileWith(OpenId, "Offen.md"));
        using ServiceProvider provider = BuildProvider(new FakeGraphService(DocumentRelations.Empty), files);
        DocumentRelationsViewModel sut = Create(provider);

        await sut.LoadAsync(OpenId, [], TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Empty(sut.FolderPath);
        Assert.False(sut.ShowFolderCommand.CanExecute(null));
        Assert.True(sut.ShowsNothingRelated);
    }

    [Fact]
    public async Task LoadAsync_DeduplicatesAndSortsTheTags()
    {
        FakeMarkdownFileRepository files = new();
        files.Add(FileWith(OpenId, "Offen.md"));
        using ServiceProvider provider = BuildProvider(new FakeGraphService(DocumentRelations.Empty), files);
        DocumentRelationsViewModel sut = Create(provider);

        await sut.LoadAsync(OpenId, ["zeta", "alpha", "ZETA"], TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(["alpha", "zeta"], sut.Tags);
    }

    [Fact]
    public async Task OpenRelatedCommand_ReportsTheDocumentToOpen()
    {
        DocumentRelations relations = new([new RelatedDocument(TargetId, "Ziel", "Ziel.md")], []);
        FakeMarkdownFileRepository files = new();
        files.Add(FileWith(OpenId, "Offen.md"));
        using ServiceProvider provider = BuildProvider(new FakeGraphService(relations), files);
        DocumentRelationsViewModel sut = Create(provider);
        await sut.LoadAsync(OpenId, [], TestContext.Current.CancellationToken).ConfigureAwait(true);
        Guid? requested = null;
        sut.OpenRequested += id => requested = id;

        sut.OpenRelatedCommand.Execute(sut.Outgoing[0]);

        Assert.Equal(TargetId, requested);
    }

    [Fact]
    public async Task ShowFolderAndShowTag_ReportWhatToShow()
    {
        FakeMarkdownFileRepository files = new();
        files.Add(FileWith(OpenId, "notizen/Offen.md"));
        using ServiceProvider provider = BuildProvider(new FakeGraphService(DocumentRelations.Empty), files);
        DocumentRelationsViewModel sut = Create(provider);
        await sut.LoadAsync(OpenId, ["projekt"], TestContext.Current.CancellationToken).ConfigureAwait(true);
        string? folder = null;
        string? tag = null;
        sut.FolderRequested += value => folder = value;
        sut.TagRequested += value => tag = value;

        sut.ShowFolderCommand.Execute(null);
        sut.ShowTagCommand.Execute("projekt");

        Assert.Equal("notizen", folder);
        Assert.Equal("projekt", tag);
    }

    [Fact]
    public async Task LoadAsync_WithoutADocument_ClearsEverything()
    {
        DocumentRelations relations = new([new RelatedDocument(TargetId, "Ziel", "Ziel.md")], []);
        FakeMarkdownFileRepository files = new();
        files.Add(FileWith(OpenId, "notizen/Offen.md"));
        using ServiceProvider provider = BuildProvider(new FakeGraphService(relations), files);
        DocumentRelationsViewModel sut = Create(provider);
        await sut.LoadAsync(OpenId, ["projekt"], TestContext.Current.CancellationToken).ConfigureAwait(true);

        await sut.LoadAsync(Guid.Empty, [], TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(sut.ShowsNoDocument);
        Assert.Empty(sut.Outgoing);
        Assert.Empty(sut.Tags);
        Assert.Empty(sut.FolderPath);
    }

    [Theory]
    [InlineData(TestFailures.Database)]
    [InlineData(TestFailures.InvalidState)]
    public async Task WhenTheLookupFails_TheAreaSaysSoInsteadOfClaimingNoRelations(string kind)
    {
        using ServiceProvider provider = BuildProvider(new FailingGraphService(TestFailures.Of(kind)), new FakeMarkdownFileRepository());
        DocumentRelationsViewModel sut = Create(provider);

        await sut.LoadAsync(OpenId, [], TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(sut.ShowsFailure);
        Assert.False(sut.IsLoading);
        Assert.False(sut.ShowsNothingRelated);
        Assert.False(sut.ShowsRelations);
        Assert.True(sut.HasDocument);
    }

    [Fact]
    public async Task WhenTheNextLookupFails_TheFileCommandsNoLongerActOnThePreviousFile()
    {
        // Die Befehle hingen an der Kennung des zuletzt geladenen Dokuments. Nach einem
        // Fehlschlag beim nächsten hätten Umbenennen und Löschen das vorige getroffen.
        SwitchableGraphService graph = new();
        FakeMarkdownFileRepository files = new();
        files.Add(FileWith(OpenId, "notizen/Offen.md"));
        using ServiceProvider provider = BuildProvider(graph, files);
        DocumentRelationsViewModel sut = Create(provider);
        await sut.LoadAsync(OpenId, [], TestContext.Current.CancellationToken).ConfigureAwait(true);
        sut.FileActions.NewName = "Neu";

        graph.Failure = new TestDbException("database is locked");
        await sut.LoadAsync(TargetId, [], TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(sut.FileActions.RenameCommand.CanExecute(null));
        Assert.False(sut.FileActions.MoveCommand.CanExecute(null));
        Assert.False(sut.FileActions.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task WhileLoading_TheFileCommandsAreDisabled()
    {
        SwitchableGraphService graph = new();
        FakeMarkdownFileRepository files = new();
        files.Add(FileWith(OpenId, "notizen/Offen.md"));
        using ServiceProvider provider = BuildProvider(graph, files);
        DocumentRelationsViewModel sut = Create(provider);
        await sut.LoadAsync(OpenId, [], TestContext.Current.CancellationToken).ConfigureAwait(true);
        sut.FileActions.NewName = "Neu";

        graph.Pending = new TaskCompletionSource<DocumentRelations>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task loading = sut.LoadAsync(TargetId, [], TestContext.Current.CancellationToken);

        Assert.True(sut.IsLoading);
        Assert.False(sut.FileActions.RenameCommand.CanExecute(null));
        Assert.False(sut.FileActions.MoveCommand.CanExecute(null));
        Assert.False(sut.FileActions.DeleteCommand.CanExecute(null));

        graph.Pending.SetResult(DocumentRelations.Empty);
        await loading.ConfigureAwait(true);
    }

    [Fact]
    public async Task WhenLoadingIsCancelled_TheAreaDoesNotKeepSayingItIsLoading()
    {
        SwitchableGraphService graph = new()
        {
            Pending = new TaskCompletionSource<DocumentRelations>(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        using ServiceProvider provider = BuildProvider(graph, new FakeMarkdownFileRepository());
        DocumentRelationsViewModel sut = Create(provider);
        using CancellationTokenSource cancellation = new();

        Task loading = sut.LoadAsync(OpenId, [], cancellation.Token);
        await cancellation.CancelAsync().ConfigureAwait(true);
        graph.Pending.SetCanceled(cancellation.Token);
        await loading.ConfigureAwait(true);

        Assert.False(sut.IsLoading);
        Assert.True(sut.ShowsNoDocument);
    }

    private static MarkdownFile FileWith(Guid id, string relativePath) => new()
    {
        Id = id,
        AbsolutePath = @"C:\notes\" + relativePath.Replace('/', '\\'),
        RelativePath = relativePath,
        FileNameWithoutExtension = Path.GetFileNameWithoutExtension(relativePath),
        ContentHash = "hash",
    };

    private static DocumentRelationsViewModel Create(ServiceProvider provider) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new RecordingDocumentFileService(),
            new FakeDialogService(),
            new StubUnsavedChangesGuard(),
            NullLogger<DocumentRelationsViewModel>.Instance);

    private static ServiceProvider BuildProvider(IGraphService graph, IMarkdownFileRepository files)
    {
        ServiceCollection services = new();
        _ = services.AddScoped(_ => graph);
        _ = services.AddScoped(_ => files);
        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>Liefert vorgegebene Verbindungen des Dokuments.</summary>
    private sealed class FakeGraphService(DocumentRelations relations) : IGraphService
    {
        public Task<GraphSnapshot> BuildSnapshotAsync(GraphFilter filter, CancellationToken cancellationToken) =>
            Task.FromResult(GraphSnapshot.Empty);

        public Task<DocumentRelations> GetRelationsAsync(Guid markdownFileId, CancellationToken cancellationToken) =>
            Task.FromResult(relations);
    }

    /// <summary>
    /// Ein Graph, der nicht antwortet. Ohne eigene Meldung sähe das aus wie ein Dokument ohne
    /// Verknüpfungen.
    /// </summary>
    private sealed class FailingGraphService(Exception failure) : IGraphService
    {
        public Task<GraphSnapshot> BuildSnapshotAsync(GraphFilter filter, CancellationToken cancellationToken) =>
            Task.FromResult(GraphSnapshot.Empty);

        public Task<DocumentRelations> GetRelationsAsync(Guid markdownFileId, CancellationToken cancellationToken) =>
            Task.FromException<DocumentRelations>(failure);
    }

    /// <summary>Ein Graph, dessen nächste Antwort der Test bestimmt: gleich, später oder als Fehler.</summary>
    private sealed class SwitchableGraphService : IGraphService
    {
        public Exception? Failure { get; set; }

        public TaskCompletionSource<DocumentRelations>? Pending { get; set; }

        public Task<GraphSnapshot> BuildSnapshotAsync(GraphFilter filter, CancellationToken cancellationToken) =>
            Task.FromResult(GraphSnapshot.Empty);

        public Task<DocumentRelations> GetRelationsAsync(Guid markdownFileId, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                return Task.FromException<DocumentRelations>(Failure);
            }
            return Pending?.Task ?? Task.FromResult(DocumentRelations.Empty);
        }
    }
}
