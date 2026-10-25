using System.Text;
using MdExplorer.App.Services;
using MdExplorer.App.ViewModels;
using MdExplorer.Search.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Das Hauptfenster: Navigation über WikiLinks, Layout und Schließen. Die Verdrahtung der Listen
/// und der Bezüge prüfen eigene Klassen.
/// </summary>
public sealed class MainViewModelTests
{
    /// <summary>Der Registerindex des Suchbereichs, dieselbe Zahl wie im ViewModel.</summary>
    private const int SearchTabIndex = 2;

    private const string TargetPath = @"C:\notes\ziel.md";
    private static readonly Guid TargetId = new("aaaaaaaa-0000-0000-0000-000000000001");

    [Fact]
    public void Construction_PassesTheFolderSelectionToTheSearch()
    {
        using MainTestHarness harness = new();

        harness.FolderTree.SelectedPathPrefix = @"projekt\sub\";

        Assert.Equal(@"projekt\sub\", harness.Search.PathPrefixFilter);

        harness.FolderTree.SelectedPathPrefix = null;

        Assert.Null(harness.Search.PathPrefixFilter);
    }

    [Fact]
    public void Dispose_UnsubscribesFromTheChildEvents()
    {
        using MainTestHarness harness = new();
        harness.FolderTree.SelectedPathPrefix = @"anfang\";

        harness.Main.Dispose();
        harness.FolderTree.SelectedPathPrefix = @"danach\";

        Assert.Equal(@"anfang\", harness.Search.PathPrefixFilter);
    }

    /// <remarks>
    /// Die Bereiche gehören dem Container, der sie erzeugt hat. Gäbe das Hauptfenster sie frei,
    /// wären sie danach für jeden anderen Nutzer tot, der sie aus demselben Container bekommt.
    /// </remarks>
    [Fact]
    public async Task Dispose_LeavesTheChildPanelsUsable()
    {
        using MainTestHarness harness = new();
        harness.Locator.SetAbsolutePath(TargetPath, TargetId);
        harness.DocRepo.Put(TargetId, RenderedDocuments.Create(TargetId, "<h1>Ziel</h1>", MainTestHarness.FixedUtc));

        harness.Main.Dispose();
        bool loaded = await harness.DocumentPanel.LoadAsync(TargetId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(loaded);
        Assert.Equal(TargetId, harness.Preview.CurrentDocumentId);
    }

    [Fact]
    public async Task NavigateToWikiLinkAsync_OnResolvedTarget_LoadsDocumentAndSelectsResult()
    {
        using MainTestHarness harness = new();
        harness.Locator.SetWikiLink("ziel", TargetId);
        harness.Locator.SetAbsolutePath(TargetPath, TargetId);
        harness.FileSystem.Files[TargetPath] = Encoding.UTF8.GetBytes("# Ziel");
        harness.DocRepo.Put(TargetId, RenderedDocuments.Create(TargetId, "<h1>Ziel</h1>", MainTestHarness.FixedUtc));
        harness.Search.Results.Add(new SearchResultItemViewModel(
            new SearchResult(TargetId, "ziel.md", "Ziel", 0.0, "<p>snippet</p>")));

        bool result = await harness.Main.NavigateToWikiLinkAsync("ziel", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(result);
        Assert.Equal(TargetId, harness.Preview.CurrentDocumentId);
        Assert.Equal(TargetId, harness.Search.SelectedResult?.MarkdownFileId);
    }

    [Fact]
    public async Task NavigateToWikiLinkAsync_OnUnresolvedTarget_ReturnsFalseAndOpensNothing()
    {
        using MainTestHarness harness = new();

        bool result = await harness.Main.NavigateToWikiLinkAsync("ungelöst", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(result);
        Assert.Null(harness.Preview.CurrentDocumentId);
        Assert.Null(harness.Search.SelectedResult);
    }

    [Fact]
    public async Task NavigateToDocumentAsync_WithoutAFile_ReportsFailure()
    {
        using MainTestHarness harness = new();

        bool result = await harness.Main.NavigateToDocumentAsync(Guid.Empty, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(result);
    }

    [Fact]
    public void ToggleTagCloud_FlipsVisibilityAndPersistsState()
    {
        using MainTestHarness harness = new();
        bool initial = harness.Main.IsTagCloudVisible;

        harness.Main.ToggleTagCloudCommand.Execute(parameter: null);

        Assert.NotEqual(initial, harness.Main.IsTagCloudVisible);
        Assert.Equal(harness.Main.IsTagCloudVisible, harness.ReadPersistedLayout().IsTagCloudVisible);

        harness.Main.ToggleTagCloudCommand.Execute(parameter: null);

        Assert.Equal(initial, harness.Main.IsTagCloudVisible);
        Assert.Equal(initial, harness.ReadPersistedLayout().IsTagCloudVisible);
    }

    [Fact]
    public void ShowSearchTab_SwitchesTheLeftColumnToTheSearch()
    {
        // Strg+F im Hauptfenster geht diesen Weg; die Stelle des Registers kennt nur das ViewModel.
        using MainTestHarness harness = new();

        harness.Main.ShowSearchTab();

        Assert.Equal(SearchTabIndex, harness.Main.LeftTabIndex);
    }

    [Fact]
    public void PersistColumnLayout_KeepsWhatOtherWindowsStoredInTheSameFile()
    {
        // Hilfefenster und Graph-Fenster schreiben ihre Felder in dieselbe Datei. Ein
        // Tab-Wechsel im Hauptfenster darf deren Stand nicht überschreiben.
        using MainTestHarness harness = new();
        WindowGeometry helpGeometry = new(10, 20, 800, 600);
        harness.SettingsStore.Save(harness.SettingsStore.Load() with { HelpWindow = helpGeometry, GraphPathPrefix = "notizen" });

        harness.Main.LeftTabIndex = SearchTabIndex;

        UiLayout persisted = harness.ReadPersistedLayout();
        Assert.Equal(helpGeometry, persisted.HelpWindow);
        Assert.Equal("notizen", persisted.GraphPathPrefix, StringComparer.Ordinal);
        Assert.Equal(SearchTabIndex, persisted.LeftTabIndex);
    }

    [Fact]
    public void RememberColumnWidths_StoresBothWidthsForTheNextStart()
    {
        using MainTestHarness harness = new();

        harness.Main.RememberColumnWidths(folderWidth: 280, previewWidth: 900);

        UiLayout persisted = harness.ReadPersistedLayout();
        Assert.Equal(280, persisted.FolderColumnWidth);
        Assert.Equal(900, persisted.PreviewColumnWidth);
    }

    [Fact]
    public void LeftTabIndex_Change_IsReadBackOnTheNextStart()
    {
        using MainTestHarness harness = new();
        Assert.Equal(0, harness.Main.LeftTabIndex);

        harness.Main.LeftTabIndex = SearchTabIndex;

        using MainViewModel restored = new(harness.Panels, harness.Services, NullLogger<MainViewModel>.Instance);
        Assert.Equal(SearchTabIndex, restored.LeftTabIndex);
    }

    [Fact]
    public void Title_AndStorageLocation_AreAvailableForTheStatusBar()
    {
        using MainTestHarness harness = new();

        Assert.Equal("MdExplorer", harness.Main.Title, StringComparer.Ordinal);
        Assert.Equal(harness.SettingsStore.StorageLocation, harness.Main.StorageLocation, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ConfirmCloseAsync_WithoutChanges_ClosesWithoutAsking()
    {
        using MainTestHarness harness = new();

        Assert.False(harness.Main.HasUnsavedChanges);
        Assert.True(await harness.Main.ConfirmCloseAsync().ConfigureAwait(true));
        Assert.Equal(0, harness.UnsavedChangesDialog.AskCount);
    }
}
