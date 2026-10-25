using System.IO;
using System.Text;
using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Abstractions;
using MdExplorer.Search.Models;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Ein Klick in einer der drei Listen öffnet die Datei; bricht der Nutzer bei ungespeicherten
/// Änderungen ab, bleibt das offene Dokument und die Liste springt zurück.
/// </summary>
/// <remarks>
/// Das Öffnen läuft ohne Rückgabewert los. Die Attrappen antworten sofort, deshalb ist es fertig,
/// wenn die Zuweisung der Auswahl zurückkehrt; was sich am UI-Thread angestellt hat, arbeitet der
/// Test mit <see cref="QueuedUiDispatcher.RunPending"/> ab.
/// </remarks>
public sealed class MainViewModelSelectionTests
{
    private const string FirstPath = @"C:\notizen\erste.md";
    private const string SecondPath = @"C:\notizen\zweite.md";
    private static readonly Guid FirstId = new("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid SecondId = new("bbbbbbbb-0000-0000-0000-000000000002");

    [Fact]
    public async Task AllFilesSelection_WithUnsavedChangesAndCancel_KeepsDocumentAndSelection()
    {
        using MainTestHarness harness = new();
        (AllFilesItemViewModel first, AllFilesItemViewModel second) = await ListTwoDocumentsAsync(harness).ConfigureAwait(true);
        harness.AllFiles.SelectedItem = first;
        MakeDirty(harness, UnsavedChangesDecision.Cancel);

        harness.AllFiles.SelectedItem = second;
        harness.UiDispatcher.RunPending();

        AssertFirstStillOpen(harness);
        Assert.Same(first, harness.AllFiles.SelectedItem);
    }

    [Fact]
    public async Task AllFilesSelection_WithUnsavedChangesAndDiscard_OpensTheNextDocument()
    {
        using MainTestHarness harness = new();
        (AllFilesItemViewModel first, AllFilesItemViewModel second) = await ListTwoDocumentsAsync(harness).ConfigureAwait(true);
        harness.AllFiles.SelectedItem = first;
        MakeDirty(harness, UnsavedChangesDecision.Discard);

        harness.AllFiles.SelectedItem = second;
        harness.UiDispatcher.RunPending();

        Assert.Equal(SecondPath, harness.Editor.FilePath);
        Assert.False(harness.Editor.IsDirty);
        Assert.Same(second, harness.AllFiles.SelectedItem);
        Assert.Equal(1, harness.UnsavedChangesDialog.AskCount);
    }

    [Fact]
    public async Task SearchSelection_WithUnsavedChangesAndCancel_KeepsDocumentAndSelection()
    {
        using MainTestHarness harness = new();
        _ = await ListTwoDocumentsAsync(harness).ConfigureAwait(true);
        SearchResultItemViewModel first = new(new SearchResult(FirstId, "erste.md", "Erste", 0.0, string.Empty));
        SearchResultItemViewModel second = new(new SearchResult(SecondId, "zweite.md", "Zweite", 0.0, string.Empty));
        harness.Search.Results.Add(first);
        harness.Search.Results.Add(second);
        harness.Search.SelectedResult = first;
        MakeDirty(harness, UnsavedChangesDecision.Cancel);

        harness.Search.SelectedResult = second;
        harness.UiDispatcher.RunPending();

        AssertFirstStillOpen(harness);
        Assert.Same(first, harness.Search.SelectedResult);
    }

    [Fact]
    public async Task FolderTreeSelection_WithUnsavedChangesAndCancel_KeepsDocumentAndSelection()
    {
        using MainTestHarness harness = new();
        _ = await ListTwoDocumentsAsync(harness).ConfigureAwait(true);
        TreeNodeViewModel first = new(FirstPath, "erste.md");
        TreeNodeViewModel second = new(SecondPath, "zweite.md");
        // Im Fenster setzt der Baum IsSelected und reicht die Auswahl dann an das ViewModel weiter.
        first.IsSelected = true;
        harness.FolderTree.SelectedNode = first;
        MakeDirty(harness, UnsavedChangesDecision.Cancel);

        first.IsSelected = false;
        second.IsSelected = true;
        harness.FolderTree.SelectedNode = second;
        harness.UiDispatcher.RunPending();

        AssertFirstStillOpen(harness);
        Assert.Same(first, harness.FolderTree.SelectedNode);
        Assert.True(first.IsSelected);
        Assert.False(second.IsSelected);
    }

    [Fact]
    public async Task NavigateToWikiLinkAsync_WithUnsavedChangesAndCancel_ReportsThatNothingWasOpened()
    {
        using MainTestHarness harness = new();
        (AllFilesItemViewModel first, _) = await ListTwoDocumentsAsync(harness).ConfigureAwait(true);
        harness.AllFiles.SelectedItem = first;
        harness.Locator.SetWikiLink("zweite", SecondId);
        MakeDirty(harness, UnsavedChangesDecision.Cancel);

        bool opened = await harness.Main.NavigateToWikiLinkAsync("zweite", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(opened);
        AssertFirstStillOpen(harness);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmCloseAsync_WithUnsavedChanges_ClosesOnlyWhenTheyMayGo(bool discard)
    {
        using MainTestHarness harness = new();
        (AllFilesItemViewModel first, _) = await ListTwoDocumentsAsync(harness).ConfigureAwait(true);
        harness.AllFiles.SelectedItem = first;
        MakeDirty(harness, discard ? UnsavedChangesDecision.Discard : UnsavedChangesDecision.Cancel);

        Assert.True(harness.Main.HasUnsavedChanges);
        Assert.Equal(discard, await harness.Main.ConfirmCloseAsync().ConfigureAwait(true));
    }

    [Fact]
    public void SelectedResult_ClearedAgain_DoesNotReloadTheDocument()
    {
        using MainTestHarness harness = new();

        harness.Search.SelectedResult = null;

        Assert.Null(harness.Preview.CurrentDocumentId);
    }

    /// <remarks>
    /// Ein Klick kostet genau eine Pfad-Abfrage: Der Dokumentbereich klärt selbst, ob die Datei
    /// im Index steht. Eine zweite Abfrage davor verdoppelte bei jedem Klick die Last auf der
    /// Datenbank, die der Indexer zur selben Zeit beschreibt.
    /// </remarks>
    [Fact]
    public async Task AllFilesSelection_LoadsTheIndexedDocumentWithOnePathLookup()
    {
        using MainTestHarness harness = new();
        (AllFilesItemViewModel first, _) = await ListTwoDocumentsAsync(harness).ConfigureAwait(true);

        harness.AllFiles.SelectedItem = first;

        Assert.Equal(FirstId, harness.Preview.CurrentDocumentId);
        Assert.Equal(1, harness.Locator.AbsolutePathCallCount);
    }

    [Fact]
    public void AllFilesSelection_OfAFileThatIsNotIndexedYet_LoadsItDirectly()
    {
        // Direkt nach dem Anlegen kennt der Indexer die Datei noch nicht. Ohne diesen
        // Ausweichpfad bliebe der Editor leer, bis der nächste Scan durchgelaufen ist.
        using MainTestHarness harness = new();
        const string FilePath = @"C:\notizen\ganz-neu.md";
        harness.FileSystem.Files[FilePath] = Encoding.UTF8.GetBytes("# Ganz neu");

        harness.AllFiles.SelectedItem = new AllFilesItemViewModel(
            new AllFilesRow(FirstId, "Ganz neu", @"notizen\ganz-neu.md", FilePath, MainTestHarness.FixedUtc, []), TimeZoneInfo.Utc);

        Assert.Equal(FilePath, harness.Editor.FilePath);
        Assert.Equal("# Ganz neu", harness.Editor.Text, StringComparer.Ordinal);
    }

    [Fact]
    public void AllFilesSelection_WhenTheDatabaseIsBusy_ShowsNothingInsteadOfFailing()
    {
        using MainTestHarness harness = new();
        harness.Locator.FailOnAbsolutePath = new TestDbException("Datenbank belegt");

        harness.AllFiles.SelectedItem = new AllFilesItemViewModel(
            new AllFilesRow(FirstId, "Bericht", @"notizen\b.md", @"C:\notizen\b.md", MainTestHarness.FixedUtc, []), TimeZoneInfo.Utc);

        Assert.Equal(1, harness.Locator.AbsolutePathCallCount);
        Assert.Null(harness.Preview.CurrentDocumentId);
        Assert.Null(harness.Editor.FilePath);
    }

    /// <summary>Zwei indizierte Dokumente, beide in der Liste „Alle Dateien“.</summary>
    private static async Task<(AllFilesItemViewModel First, AllFilesItemViewModel Second)> ListTwoDocumentsAsync(MainTestHarness harness)
    {
        foreach ((Guid id, string path, string title) in new[] { (FirstId, FirstPath, "Erste"), (SecondId, SecondPath, "Zweite") })
        {
            harness.Locator.SetAbsolutePath(path, id);
            harness.DocRepo.Put(id, RenderedDocuments.Create(id, $"<h1>{title}</h1>", MainTestHarness.FixedUtc));
            harness.FileSystem.Files[path] = Encoding.UTF8.GetBytes($"# {title}");
            harness.AllFilesQuery.Rows.Add(new AllFilesRow(id, title, Path.GetFileName(path), path, MainTestHarness.FixedUtc, []));
        }
        await harness.AllFiles.RefreshAsync().ConfigureAwait(true);

        return (
            harness.AllFiles.Items.Single(item => item.AbsolutePath == FirstPath),
            harness.AllFiles.Items.Single(item => item.AbsolutePath == SecondPath));
    }

    private static void MakeDirty(MainTestHarness harness, UnsavedChangesDecision answer)
    {
        Assert.Equal(FirstPath, harness.Editor.FilePath);
        harness.Editor.EnterEditMode();
        harness.Editor.Text = "# Erste ergänzt";
        harness.UnsavedChangesDialog.Decision = answer;
    }

    // Die Frage kommt genau einmal: Das Zurückspringen der Liste darf sie nicht erneut auslösen.
    private static void AssertFirstStillOpen(MainTestHarness harness)
    {
        Assert.Equal(FirstPath, harness.Editor.FilePath);
        Assert.Equal("# Erste ergänzt", harness.Editor.Text, StringComparer.Ordinal);
        Assert.True(harness.Editor.IsDirty);
        Assert.Equal(FirstId, harness.Preview.CurrentDocumentId);
        Assert.Equal(1, harness.UnsavedChangesDialog.AskCount);
    }
}
