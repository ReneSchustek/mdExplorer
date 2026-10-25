using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Abstractions;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Umbenennen, Verschieben und Löschen nennen vor dem Klick, wie viele Verweise brechen, fragen
/// nach ungespeicherten Änderungen und weisen einen Namen ab, der die Datei verschieben würde.
/// </summary>
public sealed class DocumentFileActionsViewModelTests
{
    private static readonly Guid OpenId = new("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void WithoutATarget_EveryCommandIsDisabled()
    {
        DocumentFileActionsViewModel sut = new(new RecordingDocumentFileService(), new FakeDialogService(), new StubUnsavedChangesGuard(), CancellationToken.None);
        sut.NewName = "Neu";

        Assert.False(sut.RenameCommand.CanExecute(null));
        Assert.False(sut.MoveCommand.CanExecute(null));
        Assert.False(sut.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public void Release_DisablesTheCommandsAndForgetsTheName()
    {
        DocumentFileActionsViewModel sut = Targeted(new RecordingDocumentFileService(), new FakeDialogService());

        sut.Release();

        Assert.False(sut.DeleteCommand.CanExecute(null));
        Assert.Empty(sut.NewName);
    }

    [Theory]
    [InlineData(@"..\Fremd")]
    [InlineData(@"C:\anders\Fremd")]
    [InlineData("unter/Fremd")]
    [InlineData("Doppel:punkt")]
    [InlineData("Frage?")]
    public async Task RenameCommand_RefusesANameThatWouldMoveTheFile(string typedName)
    {
        // Ein Pfad im neuen Namen verschöbe die Datei unter dem Etikett „Umbenennen“, ohne die
        // Rückfrage, die das Verschieben stellt.
        DocumentFileActionsViewModel sut = Targeted(new RecordingDocumentFileService(), new FakeDialogService());

        sut.NewName = typedName;

        Assert.False(sut.RenameCommand.CanExecute(null));
        Assert.True(sut.ShowsInvalidName);
    }

    [Fact]
    public async Task RenameCommand_AcceptsAPlainName()
    {
        DocumentFileActionsViewModel sut = Targeted(new RecordingDocumentFileService(), new FakeDialogService());

        sut.NewName = "Neuer Name (2026)";

        Assert.True(sut.RenameCommand.CanExecute(null));
        Assert.False(sut.ShowsInvalidName);
    }

    [Fact]
    public async Task DeleteCommand_NamesTheConsequencesBeforeTheClick()
    {
        RecordingDocumentFileService fileService = new() { Impact = new DocumentImpact("Offen", 3) };
        FakeDialogService dialog = new() { ConfirmResult = false };
        DocumentFileActionsViewModel sut = Targeted(fileService, dialog);

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
        DocumentFileActionsViewModel sut = Targeted(fileService, dialog);

        await sut.DeleteCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Contains("Kein anderes Dokument verweist darauf", dialog.LastConfirmMessage, StringComparison.Ordinal);
        Assert.True(fileService.DeleteCalled);
    }

    [Fact]
    public async Task DeleteCommand_WithASingleIncomingLink_UsesTheSingular()
    {
        RecordingDocumentFileService fileService = new() { Impact = new DocumentImpact("Offen", 1) };
        FakeDialogService dialog = new() { ConfirmResult = false };
        DocumentFileActionsViewModel sut = Targeted(fileService, dialog);

        await sut.DeleteCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Contains("Ein Dokument verweist darauf", dialog.LastConfirmMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameCommand_PassesTheTypedNameAndReportsTheOutcome()
    {
        RecordingDocumentFileService fileService = new();
        DocumentFileActionsViewModel sut = Targeted(fileService, new FakeDialogService());
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
        // Ein WikiLink zeigt auf den Dateinamen; beim Umbenennen bricht er, beim
        // Verschieben nicht. Genau darum wird hier gefragt und beim Verschieben nicht.
        RecordingDocumentFileService fileService = new() { Impact = new DocumentImpact("Offen", 2) };
        FakeDialogService dialog = new() { ConfirmResult = false };
        DocumentFileActionsViewModel sut = Targeted(fileService, dialog);

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
        DocumentFileActionsViewModel sut = Targeted(fileService, dialog);

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
        DocumentFileActionsViewModel sut = Targeted(fileService, dialog);

        await sut.MoveCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Empty(dialog.LastConfirmMessage);
        Assert.Equal(@"C:\ziel", fileService.MovedTo);
    }

    [Fact]
    public async Task RenameCommand_StaysDisabledWithoutAName()
    {
        DocumentFileActionsViewModel sut = Targeted(new RecordingDocumentFileService(), new FakeDialogService());

        sut.NewName = "   ";

        Assert.False(sut.RenameCommand.CanExecute(null));
    }

    [Fact]
    public async Task MoveCommand_WhenTheDialogIsCancelled_DoesNothing()
    {
        RecordingDocumentFileService fileService = new();
        FakeDialogService dialog = new() { DirectoryToReturn = null };
        DocumentFileActionsViewModel sut = Targeted(fileService, dialog);

        await sut.MoveCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Null(fileService.MovedTo);
        Assert.Empty(sut.OperationMessage);
    }

    [Fact]
    public async Task MoveCommand_PassesTheChosenDirectory()
    {
        RecordingDocumentFileService fileService = new();
        FakeDialogService dialog = new() { DirectoryToReturn = @"C:\ziel" };
        DocumentFileActionsViewModel sut = Targeted(fileService, dialog);

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
        DocumentFileActionsViewModel sut = Targeted(fileService, new FakeDialogService());
        bool followed = false;
        sut.DocumentChanged += _ => followed = true;

        sut.NewName = "Neu";
        await sut.RenameCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Contains("Zugriff verweigert", sut.OperationMessage, StringComparison.Ordinal);
        Assert.False(followed);
    }

    [Fact]
    public async Task FileOperations_WhenUnsavedChangesAreKept_TouchNothing()
    {
        // Umbenennen, Verschieben und Löschen laden danach die Datei neu. Bleibt der Nutzer bei
        // seinen ungespeicherten Änderungen, darf keiner der Vorgänge beginnen.
        RecordingDocumentFileService fileService = new() { Impact = new DocumentImpact("Offen", 0) };
        FakeDialogService dialog = new() { ConfirmResult = true, DirectoryToReturn = @"C:\anderswo" };
        StubUnsavedChangesGuard guard = new() { AllowLeave = false, HasUnsavedChanges = true };
        DocumentFileActionsViewModel sut = Targeted(fileService, dialog, guard);
        sut.NewName = "Neu";

        await sut.RenameCommand.ExecuteAsync(null).ConfigureAwait(true);
        await sut.MoveCommand.ExecuteAsync(null).ConfigureAwait(true);
        await sut.DeleteCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(3, guard.ConfirmCount);
        Assert.Null(fileService.RenamedTo);
        Assert.Null(fileService.MovedTo);
        Assert.False(fileService.DeleteCalled);
        Assert.Equal(0, dialog.PickDirectoryCalls);
    }

    private static DocumentFileActionsViewModel Targeted(IDocumentFileService fileService, IDialogService dialogService) =>
        Targeted(fileService, dialogService, new StubUnsavedChangesGuard());

    private static DocumentFileActionsViewModel Targeted(
        IDocumentFileService fileService,
        IDialogService dialogService,
        IUnsavedChangesGuard unsavedChanges)
    {
        DocumentFileActionsViewModel actions = new(fileService, dialogService, unsavedChanges, CancellationToken.None);
        actions.Target(OpenId, @"C:\notes\notizen\Offen.md", "Offen");
        return actions;
    }
}
