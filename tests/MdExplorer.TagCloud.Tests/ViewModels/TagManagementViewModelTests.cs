using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.Localization;
using MdExplorer.TagCloud.Models;
using MdExplorer.TagCloud.Tests.Fakes;
using MdExplorer.TagCloud.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.TagCloud.Tests.ViewModels;

/// <summary>
/// Das <see cref="TagManagementViewModel"/> gibt Umbenennen, Zusammenführen und Löschen nur mit
/// Auswahl und gültigem Namen frei, fragt vor dem Löschen nach, lädt danach neu und meldet Erfolg
/// wie Fehlschlag in der Statuszeile. Fehlt die Rückmeldung, wiederholt der Nutzer einen Vorgang,
/// der längst gelaufen ist.
/// </summary>
public sealed class TagManagementViewModelTests
{
    private static readonly DateTime FixedUtc = new(2026, 6, 12, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RefreshCommand_OnSuccess_PopulatesItemsAndSetsStatus()
    {
        FakeStatisticsService statsService = new();
        statsService.SetSnapshot(
            new TagStatistic("Docs", "docs", 3, FixedUtc),
            new TagStatistic("Build", "build", 2, FixedUtc));
        FakeManagementService managementService = new();
        FakeDialogService dialogService = new() { ShouldConfirm = true };

        TagManagementViewModel sut = new(
            statsService,
            managementService,
            dialogService,
            NullLogger<TagManagementViewModel>.Instance);

        await sut.RefreshAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(2, sut.Items.Count);
        Assert.Equal("docs", sut.Items[0].Slug);
        Assert.Equal("build", sut.Items[1].Slug);
        Assert.Equal(UiText.Format("TagManagement_Status_Loaded", 2), sut.StatusMessage);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public void CanRename_RequiresSelectionAndNewName()
    {
        TagManagementViewModel sut = CreateViewModel();

        Assert.False(sut.RenameCommand.CanExecute(null));

        sut.SelectedItem = new TagManagementItem("Docs", "docs", 3);
        Assert.False(sut.RenameCommand.CanExecute(null));

        sut.NewTagName = "neu";
        Assert.True(sut.RenameCommand.CanExecute(null));

        sut.NewTagName = "   ";
        Assert.False(sut.RenameCommand.CanExecute(null));

        sut.NewTagName = "neu";
        sut.SelectedItem = null;
        Assert.False(sut.RenameCommand.CanExecute(null));
    }

    [Fact]
    public void CanRename_WithAnInvalidName_IsDisabledAndExplained()
    {
        TagManagementViewModel sut = CreateViewModel();
        sut.SelectedItem = new TagManagementItem("Docs", "docs", 3);

        sut.NewTagName = "zwei worte";

        Assert.False(sut.RenameCommand.CanExecute(null));
        Assert.False(sut.MergeCommand.CanExecute(null));
        Assert.Equal(UiText.Format("TagManagement_Status_InvalidName", "zwei worte"), sut.StatusMessage);
    }

    [Fact]
    public void IsBusy_Change_ReevaluatesTheCommands()
    {
        TagManagementViewModel sut = CreateViewModel();
        sut.SelectedItem = new TagManagementItem("Docs", "docs", 3);
        sut.NewTagName = "neu";
        int raised = 0;
        sut.RenameCommand.CanExecuteChanged += (_, _) => raised++;

        sut.IsBusy = true;

        Assert.True(raised > 0);
        Assert.False(sut.RenameCommand.CanExecute(null));
        Assert.False(sut.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public void CanMerge_RequiresSelectionAndNewName()
    {
        TagManagementViewModel sut = CreateViewModel();

        Assert.False(sut.MergeCommand.CanExecute(null));

        sut.SelectedItem = new TagManagementItem("Docs", "docs", 3);
        Assert.False(sut.MergeCommand.CanExecute(null));

        sut.NewTagName = "target";
        Assert.True(sut.MergeCommand.CanExecute(null));

        sut.NewTagName = string.Empty;
        Assert.False(sut.MergeCommand.CanExecute(null));
    }

    [Fact]
    public void CanDelete_RequiresSelectionOnly()
    {
        TagManagementViewModel sut = CreateViewModel();

        Assert.False(sut.DeleteCommand.CanExecute(null));

        sut.SelectedItem = new TagManagementItem("Docs", "docs", 3);
        Assert.True(sut.DeleteCommand.CanExecute(null));

        sut.NewTagName = "ignored";
        Assert.True(sut.DeleteCommand.CanExecute(null));

        sut.SelectedItem = null;
        Assert.False(sut.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task DeleteCommand_OnConfirmFalse_DoesNotInvokeService()
    {
        FakeStatisticsService statsService = new();
        FakeManagementService managementService = new();
        FakeDialogService dialogService = new() { ShouldConfirm = false };

        TagManagementViewModel sut = new(
            statsService,
            managementService,
            dialogService,
            NullLogger<TagManagementViewModel>.Instance);
        sut.SelectedItem = new TagManagementItem("Docs", "docs", 3);

        await sut.DeleteAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(1, managementService.GetPreviewCount);
        Assert.Equal(0, managementService.DeleteCount);
        Assert.Null(sut.StatusMessage);
    }

    [Fact]
    public async Task RenameCommand_OnSuccess_RefreshesAndClearsNewTagName()
    {
        FakeStatisticsService statsService = new();
        statsService.SetSnapshot(new TagStatistic("Docs", "docs", 3, FixedUtc));
        FakeManagementService managementService = new();
        managementService.SetRenameResult(new TagRewriteResult("docs", FilesAffected: 2, FilesAttempted: 2, Errors: new Dictionary<string, string>()));
        FakeDialogService dialogService = new() { ShouldConfirm = true };

        TagManagementViewModel sut = new(
            statsService,
            managementService,
            dialogService,
            NullLogger<TagManagementViewModel>.Instance);
        sut.SelectedItem = new TagManagementItem("Docs", "docs", 3);
        sut.NewTagName = "neu";

        await sut.RenameAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(1, managementService.RenameCount);
        Assert.Equal("docs", managementService.LastRenameSlug);
        Assert.Equal("neu", managementService.LastRenameTarget);
        Assert.Equal(string.Empty, sut.NewTagName);
        Assert.True(statsService.CallCount >= 1, "RunWriteAsync muss nach RenameAsync die Statistik neu laden.");
        Assert.Equal(UiText.Format("TagManagement_Status_Completed", UiText.Get("TagManagement_Operation_Rename"), 2, 2, 0), sut.StatusMessage);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public void RenameCommand_OnEmptyNewTagName_CannotRun()
    {
        FakeManagementService managementService = new();
        TagManagementViewModel sut = new(
            new FakeStatisticsService(),
            managementService,
            new FakeDialogService { ShouldConfirm = true },
            NullLogger<TagManagementViewModel>.Instance);
        sut.SelectedItem = new TagManagementItem("Docs", "docs", 3);
        sut.NewTagName = string.Empty;

        // Gesperrt: Die Schaltfläche ruft den Befehl dann nicht auf.
        Assert.False(sut.RenameCommand.CanExecute(null));
        Assert.False(sut.MergeCommand.CanExecute(null));
        Assert.Equal(0, managementService.RenameCount);
    }

    [Fact]
    public async Task MergeCommand_RoutesToMergeService()
    {
        FakeStatisticsService statsService = new();
        statsService.SetSnapshot(new TagStatistic("Docs", "docs", 3, FixedUtc));
        FakeManagementService managementService = new();
        managementService.SetMergeResult(new TagRewriteResult("source", FilesAffected: 1, FilesAttempted: 1, Errors: new Dictionary<string, string>()));
        FakeDialogService dialogService = new() { ShouldConfirm = true };

        TagManagementViewModel sut = new(
            statsService,
            managementService,
            dialogService,
            NullLogger<TagManagementViewModel>.Instance);
        sut.SelectedItem = new TagManagementItem("Source", "source", 1);
        sut.NewTagName = "target";

        await sut.MergeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(1, managementService.MergeCount);
        Assert.Equal("source", managementService.LastMergeSlug);
        Assert.Equal("target", managementService.LastMergeTarget);
        Assert.Equal(0, managementService.RenameCount);
        Assert.Equal(0, managementService.DeleteCount);
    }

    [Fact]
    public async Task MergeCommand_OnSuccess_SetsMergeStatusMessage()
    {
        FakeStatisticsService statsService = new();
        statsService.SetSnapshot(new TagStatistic("Target", "target", 5, FixedUtc));
        FakeManagementService managementService = new();
        managementService.SetMergeResult(new TagRewriteResult("source", FilesAffected: 1, FilesAttempted: 1, Errors: new Dictionary<string, string>()));
        FakeDialogService dialogService = new() { ShouldConfirm = true };

        TagManagementViewModel sut = new(
            statsService,
            managementService,
            dialogService,
            NullLogger<TagManagementViewModel>.Instance);
        sut.SelectedItem = new TagManagementItem("Source", "source", 1);
        sut.NewTagName = "target";

        await sut.MergeAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(UiText.Format("TagManagement_Status_Completed", UiText.Get("TagManagement_Operation_Merge"), 1, 1, 0), sut.StatusMessage);
        Assert.Equal(string.Empty, sut.NewTagName);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task DeleteCommand_OnSuccess_SetsDeleteStatusMessage()
    {
        FakeStatisticsService statsService = new();
        statsService.SetSnapshot(new TagStatistic("Keep", "keep", 2, FixedUtc));
        FakeManagementService managementService = new();
        managementService.SetDeleteResult(new TagRewriteResult("kill", FilesAffected: 1, FilesAttempted: 1, Errors: new Dictionary<string, string>()));
        FakeDialogService dialogService = new() { ShouldConfirm = true };

        TagManagementViewModel sut = new(
            statsService,
            managementService,
            dialogService,
            NullLogger<TagManagementViewModel>.Instance);
        sut.SelectedItem = new TagManagementItem("Kill", "kill", 1);

        await sut.DeleteAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(1, managementService.DeleteCount);
        Assert.Equal(UiText.Format("TagManagement_Status_Completed", UiText.Get("TagManagement_Operation_Delete"), 1, 1, 0), sut.StatusMessage);
        Assert.False(sut.IsBusy);
    }

    private static TagManagementViewModel CreateViewModel() =>
        new(
            new FakeStatisticsService(),
            new FakeManagementService(),
            new FakeDialogService(),
            NullLogger<TagManagementViewModel>.Instance);

    [Fact]
    public async Task RefreshCommand_WhenTheDatabaseIsBusy_KeepsTheListAndReportsIt()
    {
        // Die zuvor geladene Liste muss stehen bleiben; ein leeres Fenster sähe so aus,
        // als gäbe es gar keine Tags mehr.
        FakeStatisticsService statsService = new();
        statsService.SetSnapshot(new TagStatistic("Docs", "docs", 3, FixedUtc));
        FakeManagementService managementService = new();
        TagManagementViewModel sut = new(statsService, managementService, new FakeDialogService(), NullLogger<TagManagementViewModel>.Instance);
        await sut.RefreshAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        statsService.Failure = new TestDbException("Datenbank belegt");

        await sut.RefreshAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = Assert.Single(sut.Items);
        Assert.Equal(UiText.Get("TagManagement_Status_LoadFailed"), sut.StatusMessage);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task RefreshCommand_WhenCancelled_LeavesNoFailureMessage()
    {
        FakeStatisticsService statsService = new()
        {
            Failure = new OperationCanceledException(),
        };
        FakeManagementService managementService = new();
        TagManagementViewModel sut = new(statsService, managementService, new FakeDialogService(), NullLogger<TagManagementViewModel>.Instance);

        await sut.RefreshAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.NotEqual(UiText.Get("TagManagement_Status_LoadFailed"), sut.StatusMessage);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task DeleteCommand_WhenTheDatabaseIsBusy_ReportsTheFailureAndStaysUsable()
    {
        // Ohne Rückmeldung sieht der Nutzer nur, dass sich nichts tut, und löscht erneut.
        FakeStatisticsService statsService = new();
        FakeManagementService managementService = new()
        {
            DeleteFailure = new TestDbException("Datenbank belegt"),
        };
        FakeDialogService dialogService = new() { ShouldConfirm = true };
        TagManagementViewModel sut = new(statsService, managementService, dialogService, NullLogger<TagManagementViewModel>.Instance)
        {
            SelectedItem = new TagManagementItem("Docs", "docs", 3),
        };

        await sut.DeleteCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(UiText.Format("TagManagement_Status_Failed", UiText.Get("TagManagement_Operation_Delete")), sut.StatusMessage);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task DeleteCommand_WhenThePreviewFails_ReportsItWithoutAsking()
    {
        FakeManagementService managementService = new()
        {
            PreviewFailure = new TestDbException("Datenbank belegt"),
        };
        FakeDialogService dialogService = new() { ShouldConfirm = true };
        TagManagementViewModel sut = new(new FakeStatisticsService(), managementService, dialogService, NullLogger<TagManagementViewModel>.Instance)
        {
            SelectedItem = new TagManagementItem("Docs", "docs", 3),
        };

        await sut.DeleteCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(UiText.Format("TagManagement_Status_Failed", UiText.Get("TagManagement_Operation_Delete")), sut.StatusMessage);
        Assert.Equal(0, managementService.DeleteCount);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task DeleteCommand_WhenTheReloadFails_StillReportsTheResult()
    {
        // Der Schreibvorgang war erfolgreich. Scheitert danach nur das Nachladen der Liste,
        // darf die Erfolgsmeldung nicht verloren gehen, sonst hält der Nutzer den
        // Löschvorgang fälschlich für gescheitert und wiederholt ihn.
        FakeStatisticsService statsService = new();
        FakeManagementService managementService = new();
        managementService.SetDeleteResult(new TagRewriteResult("docs", 4, 5, new Dictionary<string, string>()));
        FakeDialogService dialogService = new() { ShouldConfirm = true };
        TagManagementViewModel sut = new(statsService, managementService, dialogService, NullLogger<TagManagementViewModel>.Instance)
        {
            SelectedItem = new TagManagementItem("Docs", "docs", 3),
        };
        statsService.Failure = new TestDbException("Datenbank belegt");

        await sut.DeleteCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(UiText.Format("TagManagement_Status_Completed", UiText.Get("TagManagement_Operation_Delete"), 4, 5, 0), sut.StatusMessage);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task DeleteCommand_WhenCancelled_LeavesNoFailureMessage()
    {
        FakeStatisticsService statsService = new();
        FakeManagementService managementService = new()
        {
            DeleteFailure = new OperationCanceledException(),
        };
        FakeDialogService dialogService = new() { ShouldConfirm = true };
        TagManagementViewModel sut = new(statsService, managementService, dialogService, NullLogger<TagManagementViewModel>.Instance)
        {
            SelectedItem = new TagManagementItem("Docs", "docs", 3),
        };

        await sut.DeleteCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.NotEqual(UiText.Format("TagManagement_Status_Failed", UiText.Get("TagManagement_Operation_Delete")), sut.StatusMessage);
        Assert.False(sut.IsBusy);
    }

    /// <summary><see cref="System.Data.Common.DbException"/> ist abstrakt; der Fehlerpfad braucht eine eigene Ausprägung.</summary>

    /// <summary>Statistik mit vorgegebenem Stand, die ihre Abrufe zählt.</summary>
    private sealed class FakeStatisticsService : ITagStatisticsService
    {
        private IReadOnlyList<TagStatistic> _snapshot = [];
        private int _callCount;

        public int CallCount => _callCount;

        public void SetSnapshot(params TagStatistic[] tags) => _snapshot = tags;

        /// <summary>Fehler, den jeder Abruf liefert, solange er gesetzt ist; für den Ausweichpfad beim Nachladen.</summary>
        public Exception? Failure { get; set; }

        public Task<IReadOnlyList<TagStatistic>> GetTopTagsAsync(int topN, CancellationToken cancellationToken)
        {
            _callCount++;
            return Failure is not null
                ? Task.FromException<IReadOnlyList<TagStatistic>>(Failure)
                : Task.FromResult(_snapshot);
        }
    }

    /// <summary>Schlagwortpflege, die Aufrufe und Argumente mitschreibt und vorgegebene Ergebnisse liefert.</summary>
    private sealed class FakeManagementService : ITagManagementService
    {
        private TagRewriteResult _renameResult = new("noop", 0, 0, new Dictionary<string, string>());
        private TagRewriteResult _mergeResult = new("noop", 0, 0, new Dictionary<string, string>());
        private TagRewriteResult _deleteResult = new("noop", 0, 0, new Dictionary<string, string>());

        public int GetPreviewCount { get; private set; }
        public int RenameCount { get; private set; }
        public int MergeCount { get; private set; }
        public int DeleteCount { get; private set; }
        public string? LastRenameSlug { get; private set; }
        public string? LastRenameTarget { get; private set; }
        public string? LastMergeSlug { get; private set; }
        public string? LastMergeTarget { get; private set; }

        public void SetRenameResult(TagRewriteResult result) => _renameResult = result;
        public void SetMergeResult(TagRewriteResult result) => _mergeResult = result;
        public void SetDeleteResult(TagRewriteResult result) => _deleteResult = result;

        /// <summary>Fehler, den <see cref="GetPreviewAsync"/> statt einer Vorschau liefert.</summary>
        public Exception? PreviewFailure { get; set; }

        public Task<TagPreview> GetPreviewAsync(string slug, CancellationToken cancellationToken)
        {
            GetPreviewCount++;
            return PreviewFailure is not null
                ? Task.FromException<TagPreview>(PreviewFailure)
                : Task.FromResult(new TagPreview(slug, FileCount: 0, SamplePaths: Array.Empty<string>()));
        }

        public Task<TagRewriteResult> RenameAsync(string oldSlug, string newRawName, CancellationToken cancellationToken)
        {
            RenameCount++;
            LastRenameSlug = oldSlug;
            LastRenameTarget = newRawName;
            return Task.FromResult(_renameResult);
        }

        public Task<TagRewriteResult> MergeAsync(string sourceSlug, string targetRawName, CancellationToken cancellationToken)
        {
            MergeCount++;
            LastMergeSlug = sourceSlug;
            LastMergeTarget = targetRawName;
            return Task.FromResult(_mergeResult);
        }

        /// <summary>Fehler, den <see cref="DeleteAsync"/> statt eines Ergebnisses liefert.</summary>
        public Exception? DeleteFailure { get; set; }

        public Task<TagRewriteResult> DeleteAsync(string slug, CancellationToken cancellationToken)
        {
            DeleteCount++;
            return DeleteFailure is not null
                ? Task.FromException<TagRewriteResult>(DeleteFailure)
                : Task.FromResult(_deleteResult);
        }
    }

    /// <summary>Rückfrage, deren Antwort der Test festlegt.</summary>
    private sealed class FakeDialogService : ITagManagementDialogService
    {
        public bool ShouldConfirm { get; set; }

        public bool Confirm(string title, string message) => ShouldConfirm;
    }
}
