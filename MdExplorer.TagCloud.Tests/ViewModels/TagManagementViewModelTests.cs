using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.Models;
using MdExplorer.TagCloud.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.TagCloud.Tests.ViewModels;

/// <summary>Unit-Tests des <see cref="TagManagementViewModel"/>.</summary>
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

        await sut.RefreshAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, sut.Items.Count);
        Assert.Equal("docs", sut.Items[0].Slug);
        Assert.Equal("build", sut.Items[1].Slug);
        Assert.Equal("2 Tag(s) geladen.", sut.StatusMessage);
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

        await sut.DeleteAsync(CancellationToken.None).ConfigureAwait(true);

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

        await sut.RenameAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, managementService.RenameCount);
        Assert.Equal("docs", managementService.LastRenameSlug);
        Assert.Equal("neu", managementService.LastRenameTarget);
        Assert.Equal(string.Empty, sut.NewTagName);
        Assert.True(statsService.CallCount >= 1, "RunWriteAsync muss nach RenameAsync die Statistik neu laden.");
        Assert.Equal("Tag umbenennen: 2 von 2 Datei(en) geaendert. Fehler: 0.", sut.StatusMessage);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task RenameCommand_OnEmptyNewTagName_ThrowsArgumentException()
    {
        FakeStatisticsService statsService = new();
        FakeManagementService managementService = new();
        managementService.RenameValidatesArguments = true;
        FakeDialogService dialogService = new() { ShouldConfirm = true };

        TagManagementViewModel sut = new(
            statsService,
            managementService,
            dialogService,
            NullLogger<TagManagementViewModel>.Instance);
        sut.SelectedItem = new TagManagementItem("Docs", "docs", 3);
        sut.NewTagName = string.Empty;

        _ = await Assert.ThrowsAsync<ArgumentException>(() => sut.RenameAsync(CancellationToken.None)).ConfigureAwait(true);
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

        await sut.MergeAsync(CancellationToken.None).ConfigureAwait(true);

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

        await sut.MergeAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("Tags zusammenfuehren: 1 von 1 Datei(en) geaendert. Fehler: 0.", sut.StatusMessage);
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

        await sut.DeleteAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, managementService.DeleteCount);
        Assert.Equal("Tag loeschen: 1 von 1 Datei(en) geaendert. Fehler: 0.", sut.StatusMessage);
        Assert.False(sut.IsBusy);
    }

    private static TagManagementViewModel CreateViewModel() =>
        new(
            new FakeStatisticsService(),
            new FakeManagementService(),
            new FakeDialogService(),
            NullLogger<TagManagementViewModel>.Instance);

    private sealed class FakeStatisticsService : ITagStatisticsService
    {
        private IReadOnlyList<TagStatistic> _snapshot = [];
        private int _callCount;

        public int CallCount => _callCount;

        public void SetSnapshot(params TagStatistic[] tags) => _snapshot = tags;

        public Task<IReadOnlyList<TagStatistic>> GetTopTagsAsync(int topN, CancellationToken cancellationToken)
        {
            _callCount++;
            return Task.FromResult(_snapshot);
        }
    }

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
        public bool RenameValidatesArguments { get; set; }

        public void SetRenameResult(TagRewriteResult result) => _renameResult = result;
        public void SetMergeResult(TagRewriteResult result) => _mergeResult = result;
        public void SetDeleteResult(TagRewriteResult result) => _deleteResult = result;

        public Task<TagPreview> GetPreviewAsync(string slug, CancellationToken cancellationToken)
        {
            GetPreviewCount++;
            return Task.FromResult(new TagPreview(slug, FileCount: 0, SamplePaths: Array.Empty<string>()));
        }

        public Task<TagRewriteResult> RenameAsync(string oldSlug, string newRawName, CancellationToken cancellationToken)
        {
            if (RenameValidatesArguments)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(newRawName);
            }
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

        public Task<TagRewriteResult> DeleteAsync(string slug, CancellationToken cancellationToken)
        {
            DeleteCount++;
            return Task.FromResult(_deleteResult);
        }
    }

    private sealed class FakeDialogService : ITagManagementDialogService
    {
        public bool ShouldConfirm { get; set; }

        public bool Confirm(string title, string message) => ShouldConfirm;
    }
}
