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
/// Tests für den Zusammenhangs-Bereich am geöffneten Dokument.
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

    [Fact]
    public async Task WhenTheLookupFails_TheAreaSaysSoInsteadOfClaimingNoRelations()
    {
        using ServiceProvider provider = BuildProvider(new FailingGraphService(), new FakeMarkdownFileRepository());
        DocumentRelationsViewModel sut = Create(provider);

        await sut.LoadAsync(OpenId, [], TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(sut.ShowsNothingRelated);
        Assert.False(sut.ShowsRelations);
        Assert.True(sut.HasDocument);
    }

    [Fact]
    public async Task DeleteCommand_NamesTheConsequencesBeforeTheClick()
    {
        RecordingDocumentFileService fileService = new() { Impact = new DocumentImpact("Offen", 3) };
        FakeDialogService dialog = new() { ConfirmResult = false };
        DocumentRelationsViewModel sut = await LoadedAsync(fileService, dialog).ConfigureAwait(true);

        await sut.DeleteCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Contains("3 Dokumente verweisen darauf", dialog.LastConfirmMessage, StringComparison.Ordinal);
        // Abgelehnt heißt abgelehnt: Es darf nichts passiert sein.
        Assert.False(fileService.DeleteCalled);
    }

    [Fact]
    public async Task DeleteCommand_WithoutIncomingLinks_SaysSoPlainly()
    {
        RecordingDocumentFileService fileService = new() { Impact = new DocumentImpact("Offen", 0) };
        FakeDialogService dialog = new() { ConfirmResult = true };
        DocumentRelationsViewModel sut = await LoadedAsync(fileService, dialog).ConfigureAwait(true);

        await sut.DeleteCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Contains("Kein anderes Dokument verweist darauf", dialog.LastConfirmMessage, StringComparison.Ordinal);
        Assert.True(fileService.DeleteCalled);
    }

    [Fact]
    public async Task DeleteCommand_WithASingleIncomingLink_UsesTheSingular()
    {
        RecordingDocumentFileService fileService = new() { Impact = new DocumentImpact("Offen", 1) };
        FakeDialogService dialog = new() { ConfirmResult = false };
        DocumentRelationsViewModel sut = await LoadedAsync(fileService, dialog).ConfigureAwait(true);

        await sut.DeleteCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Contains("Ein Dokument verweist darauf", dialog.LastConfirmMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameCommand_PassesTheTypedNameAndReportsTheOutcome()
    {
        RecordingDocumentFileService fileService = new();
        DocumentRelationsViewModel sut = await LoadedAsync(fileService, new FakeDialogService()).ConfigureAwait(true);
        string? changedTo = "unverändert";
        sut.DocumentChanged += path => changedTo = path;

        sut.NewName = "Neuer Name";
        await sut.RenameCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal("Neuer Name", fileService.RenamedTo);
        Assert.Equal("Erledigt.", sut.OperationMessage);
        Assert.Equal(@"C:\notes\Neu.md", changedTo);
    }

    [Fact]
    public async Task RenameCommand_WithIncomingLinks_AsksBeforeBreakingThem()
    {
        // Ein WikiLink zeigt auf den Dateinamen — beim Umbenennen bricht er, beim
        // Verschieben nicht. Genau darum wird hier gefragt und beim Verschieben nicht.
        RecordingDocumentFileService fileService = new() { Impact = new DocumentImpact("Offen", 2) };
        FakeDialogService dialog = new() { ConfirmResult = false };
        DocumentRelationsViewModel sut = await LoadedAsync(fileService, dialog).ConfigureAwait(true);

        sut.NewName = "Anders";
        await sut.RenameCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Contains("2 Dokumente verweisen auf den bisherigen Namen", dialog.LastConfirmMessage, StringComparison.Ordinal);
        Assert.Null(fileService.RenamedTo);
    }

    [Fact]
    public async Task RenameCommand_WithoutIncomingLinks_AsksNothing()
    {
        RecordingDocumentFileService fileService = new() { Impact = new DocumentImpact("Offen", 0) };
        FakeDialogService dialog = new() { ConfirmResult = false };
        DocumentRelationsViewModel sut = await LoadedAsync(fileService, dialog).ConfigureAwait(true);

        sut.NewName = "Anders";
        await sut.RenameCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Empty(dialog.LastConfirmMessage);
        Assert.Equal("Anders", fileService.RenamedTo);
    }

    [Fact]
    public async Task MoveCommand_AsksNothing_BecauseLinksSurviveAMove()
    {
        RecordingDocumentFileService fileService = new() { Impact = new DocumentImpact("Offen", 5) };
        FakeDialogService dialog = new() { DirectoryToReturn = @"C:\ziel", ConfirmResult = false };
        DocumentRelationsViewModel sut = await LoadedAsync(fileService, dialog).ConfigureAwait(true);

        await sut.MoveCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Empty(dialog.LastConfirmMessage);
        Assert.Equal(@"C:\ziel", fileService.MovedTo);
    }

    [Fact]
    public async Task RenameCommand_StaysDisabledWithoutAName()
    {
        DocumentRelationsViewModel sut = await LoadedAsync(new RecordingDocumentFileService(), new FakeDialogService()).ConfigureAwait(true);

        sut.NewName = "   ";

        Assert.False(sut.RenameCommand.CanExecute(null));
    }

    [Fact]
    public async Task MoveCommand_WhenTheDialogIsCancelled_DoesNothing()
    {
        RecordingDocumentFileService fileService = new();
        FakeDialogService dialog = new() { DirectoryToReturn = null };
        DocumentRelationsViewModel sut = await LoadedAsync(fileService, dialog).ConfigureAwait(true);

        await sut.MoveCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Null(fileService.MovedTo);
        Assert.Empty(sut.OperationMessage);
    }

    [Fact]
    public async Task MoveCommand_PassesTheChosenDirectory()
    {
        RecordingDocumentFileService fileService = new();
        FakeDialogService dialog = new() { DirectoryToReturn = @"C:\ziel" };
        DocumentRelationsViewModel sut = await LoadedAsync(fileService, dialog).ConfigureAwait(true);

        await sut.MoveCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(@"C:\ziel", fileService.MovedTo);
    }

    [Fact]
    public async Task WhenAnOperationFails_TheMessageIsShownAndNothingFollows()
    {
        RecordingDocumentFileService fileService = new()
        {
            Result = DocumentFileResult.Failed("Die Datei ließ sich nicht umbenennen: Zugriff verweigert"),
        };
        DocumentRelationsViewModel sut = await LoadedAsync(fileService, new FakeDialogService()).ConfigureAwait(true);
        bool followed = false;
        sut.DocumentChanged += _ => followed = true;

        sut.NewName = "Neu";
        await sut.RenameCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Contains("Zugriff verweigert", sut.OperationMessage, StringComparison.Ordinal);
        Assert.False(followed);
    }

    private static async Task<DocumentRelationsViewModel> LoadedAsync(
        IDocumentFileService fileService,
        IDialogService dialogService)
    {
        FakeMarkdownFileRepository files = new();
        files.Add(FileWith(OpenId, "notizen/Offen.md"));
        ServiceProvider provider = BuildProvider(new FakeGraphService(DocumentRelations.Empty), files);
        DocumentRelationsViewModel viewModel = Create(provider, fileService, dialogService);
        await viewModel.LoadAsync(OpenId, [], TestContext.Current.CancellationToken).ConfigureAwait(true);

        return viewModel;
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
        Create(provider, new RecordingDocumentFileService(), new FakeDialogService());

    private static DocumentRelationsViewModel Create(
        ServiceProvider provider,
        IDocumentFileService fileService,
        IDialogService dialogService) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            fileService,
            dialogService,
            NullLogger<DocumentRelationsViewModel>.Instance);

    private static ServiceProvider BuildProvider(IGraphService graph, IMarkdownFileRepository files)
    {
        ServiceCollection services = new();
        _ = services.AddScoped(_ => graph);
        _ = services.AddScoped(_ => files);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class FakeGraphService(DocumentRelations relations) : IGraphService
    {
        public Task<GraphSnapshot> BuildSnapshotAsync(GraphFilter filter, CancellationToken cancellationToken) =>
            Task.FromResult(GraphSnapshot.Empty);

        public Task<DocumentRelations> GetRelationsAsync(Guid markdownFileId, CancellationToken cancellationToken) =>
            Task.FromResult(relations);
    }

    /// <summary>Ein Graph, der nicht antwortet — die Lage, die wie „nichts verknüpft" aussieht.</summary>
    private sealed class FailingGraphService : IGraphService
    {
        public Task<GraphSnapshot> BuildSnapshotAsync(GraphFilter filter, CancellationToken cancellationToken) =>
            Task.FromResult(GraphSnapshot.Empty);

        public Task<DocumentRelations> GetRelationsAsync(Guid markdownFileId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Der Index ist nicht ansprechbar.");
    }
}
