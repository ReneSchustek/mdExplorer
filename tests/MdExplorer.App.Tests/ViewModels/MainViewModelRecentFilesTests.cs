using System.Text;
using MdExplorer.App.Services;
using MdExplorer.App.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Der Verlauf hält jede geöffnete Datei fest, gleich woher sie kam, überlebt den Neustart und
/// öffnet über denselben Weg wie die Liste. „Verlauf leeren“ leert auch die Sprungliste.
/// </summary>
public sealed class MainViewModelRecentFilesTests
{
    private const string FirstPath = @"D:\Ablage\Erste.md";
    private const string SecondPath = @"D:\Ablage\Zweite.md";

    [Fact]
    public async Task OpeningAFile_PutsItIntoTheHistory_AndTheJumpList()
    {
        using MainTestHarness harness = CreateWithTwoFiles();

        _ = await harness.Main.OpenFileAsync(FirstPath).ConfigureAwait(true);
        _ = await harness.Main.OpenFileAsync(SecondPath).ConfigureAwait(true);

        Assert.Equal([SecondPath, FirstPath], harness.RecentFiles.Items.Select(item => item.FullPath));
        Assert.Equal([SecondPath, FirstPath], harness.JumpList.Last);
        Assert.True(harness.RecentFiles.Items[0].Exists);
        Assert.Equal("Zweite.md", harness.RecentFiles.Items[0].Name, StringComparer.Ordinal);
        Assert.Equal(@"D:\Ablage", harness.RecentFiles.Items[0].Folder, StringComparer.Ordinal);
    }

    [Fact]
    public async Task TheHistory_SurvivesARestart()
    {
        using MainTestHarness harness = CreateWithTwoFiles();
        _ = await harness.Main.OpenFileAsync(FirstPath).ConfigureAwait(true);

        RecentFilesViewModel restarted = new(harness.SettingsStore, harness.FileSystem, harness.JumpList, NullLogger<RecentFilesViewModel>.Instance);

        Assert.Equal([FirstPath], restarted.Items.Select(item => item.FullPath));
    }

    [Fact]
    public async Task OtherLayoutValues_StayWhenTheHistoryIsSaved()
    {
        using MainTestHarness harness = CreateWithTwoFiles();
        harness.SettingsStore.Save(harness.SettingsStore.Load() with { GraphPathPrefix = "notizen/" });

        _ = await harness.Main.OpenFileAsync(FirstPath).ConfigureAwait(true);

        Assert.Equal("notizen/", harness.ReadPersistedLayout().GraphPathPrefix, StringComparer.Ordinal);
    }

    [Fact]
    public async Task OpenCommand_OpensTheEntryInTheDocumentArea()
    {
        using MainTestHarness harness = CreateWithTwoFiles();
        _ = await harness.Main.OpenFileAsync(FirstPath).ConfigureAwait(true);
        _ = await harness.Main.OpenFileAsync(SecondPath).ConfigureAwait(true);
        RecentFileItem first = harness.RecentFiles.Items.Single(item => item.FullPath == FirstPath);

        harness.RecentFiles.OpenCommand.Execute(first);
        await WaitUntilAsync(() => harness.Editor.FilePath == FirstPath).ConfigureAwait(true);

        Assert.Equal(FirstPath, harness.Editor.FilePath);
        Assert.Equal(FirstPath, harness.RecentFiles.Items[0].FullPath);
    }

    [Fact]
    public async Task OpenCommand_WithUnsavedChangesAndCancel_KeepsTheEditedDocument()
    {
        using MainTestHarness harness = CreateWithTwoFiles();
        _ = await harness.Main.OpenFileAsync(FirstPath).ConfigureAwait(true);
        _ = await harness.Main.OpenFileAsync(SecondPath).ConfigureAwait(true);
        harness.Editor.EnterEditMode();
        harness.Editor.Text = "# Zweite ergänzt";
        harness.UnsavedChangesDialog.Decision = UnsavedChangesDecision.Cancel;

        harness.RecentFiles.OpenCommand.Execute(harness.RecentFiles.Items.Single(item => item.FullPath == FirstPath));
        await WaitUntilAsync(() => harness.UnsavedChangesDialog.AskCount == 1).ConfigureAwait(true);

        Assert.Equal(SecondPath, harness.Editor.FilePath);
        Assert.True(harness.Editor.IsDirty);
    }

    [Fact]
    public async Task OpenCommand_ForAFileThatIsGone_RemovesTheEntryInsteadOfOpening()
    {
        using MainTestHarness harness = CreateWithTwoFiles();
        _ = await harness.Main.OpenFileAsync(FirstPath).ConfigureAwait(true);
        _ = await harness.Main.OpenFileAsync(SecondPath).ConfigureAwait(true);
        _ = harness.FileSystem.Files.Remove(FirstPath);
        harness.RecentFiles.Refresh();
        RecentFileItem gone = harness.RecentFiles.Items.Single(item => item.FullPath == FirstPath);
        Assert.False(gone.Exists);

        harness.RecentFiles.OpenCommand.Execute(gone);

        Assert.Equal([SecondPath], harness.RecentFiles.Items.Select(item => item.FullPath));
        Assert.Equal(SecondPath, harness.Editor.FilePath);
        Assert.Equal([SecondPath], harness.JumpList.Last);
    }

    [Fact]
    public async Task RemoveCommand_TakesOneEntryOut()
    {
        using MainTestHarness harness = CreateWithTwoFiles();
        _ = await harness.Main.OpenFileAsync(FirstPath).ConfigureAwait(true);
        _ = await harness.Main.OpenFileAsync(SecondPath).ConfigureAwait(true);

        harness.RecentFiles.RemoveCommand.Execute(harness.RecentFiles.Items[0]);

        Assert.Equal([FirstPath], harness.RecentFiles.Items.Select(item => item.FullPath));
    }

    [Fact]
    public async Task ClearCommand_EmptiesTheHistory_TheStoredLayout_AndTheJumpList()
    {
        using MainTestHarness harness = CreateWithTwoFiles();
        _ = await harness.Main.OpenFileAsync(FirstPath).ConfigureAwait(true);
        Assert.True(harness.RecentFiles.ClearCommand.CanExecute(null));

        harness.RecentFiles.ClearCommand.Execute(null);

        Assert.Empty(harness.RecentFiles.Items);
        Assert.False(harness.RecentFiles.HasItems);
        Assert.False(harness.RecentFiles.ClearCommand.CanExecute(null));
        Assert.Empty(harness.ReadPersistedLayout().RecentFiles ?? ["kein Stand"]);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<string>>(harness.JumpList.Last));
    }

    [Fact]
    public void Commands_WithoutAnItem_DoNothing()
    {
        using MainTestHarness harness = CreateWithTwoFiles();

        harness.RecentFiles.OpenCommand.Execute(null);
        harness.RecentFiles.RemoveCommand.Execute(null);

        Assert.Empty(harness.JumpList.Shown);
    }

    [Fact]
    public void Record_WithoutPath_Throws()
    {
        using MainTestHarness harness = CreateWithTwoFiles();

        _ = Assert.Throws<ArgumentException>(() => harness.RecentFiles.Record(" "));
    }

    private static MainTestHarness CreateWithTwoFiles()
    {
        MainTestHarness harness = new();
        harness.FileSystem.Files[FirstPath] = Encoding.UTF8.GetBytes("# Erste");
        harness.FileSystem.Files[SecondPath] = Encoding.UTF8.GetBytes("# Zweite");
        return harness;
    }

    // Der Befehl öffnet über das Hauptfenster, das den Auftrag ohne await weiterreicht. Die
    // Obergrenze hält nur einen Test an, der sonst nie endete; über die Geschwindigkeit sagt sie
    // nichts, das Warten endet mit der Bedingung.
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 3000 && !condition(); attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken).ConfigureAwait(true);
        }
    }
}
