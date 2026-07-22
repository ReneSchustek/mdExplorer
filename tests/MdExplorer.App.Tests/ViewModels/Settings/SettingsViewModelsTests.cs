using System.IO;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels.Settings;
using MdExplorer.Core.Models;
using MdExplorer.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.ViewModels.Settings;

/// <summary>Tests für die Settings-Tab-ViewModels und deren Orchestrierung.</summary>
public sealed class SettingsViewModelsTests
{
    [Fact]
    public void AppearanceTab_Roundtrip_PreservesValues()
    {
        AppearanceSettings initial = new(AppTheme.Dark, 20, 75);

        AppearanceTabViewModel sut = new(initial);

        Assert.Equal(AppTheme.Dark, sut.Theme);
        Assert.Equal(20, sut.PreviewFontSize);
        Assert.Equal(75, sut.ResultsPerPage);
        Assert.Equal(initial, sut.ToSettings());
        Assert.Equal(3, AppearanceTabViewModel.AvailableThemes.Count);
    }

    [Fact]
    public void AppearanceTab_AfterEdit_ToSettingsReflectsChanges()
    {
        AppearanceTabViewModel sut = new(AppearanceSettings.Default) { Theme = AppTheme.Light, PreviewFontSize = 32 };

        AppearanceSettings result = sut.ToSettings();

        Assert.Equal(AppTheme.Light, result.Theme);
        Assert.Equal(32, result.PreviewFontSize);
    }

    [Fact]
    public void BehaviorTab_Roundtrip_PreservesValues()
    {
        BehaviorSettings initial = new(500, 120, CheckForUpdatesAtStartup: false);

        BehaviorTabViewModel sut = new(initial);

        Assert.Equal(500, sut.SearchDebounceMs);
        Assert.Equal(120, sut.IndexerResyncIntervalSeconds);
        Assert.False(sut.CheckForUpdatesAtStartup);
        Assert.Equal(initial, sut.ToSettings());
    }

    [Fact]
    public void IndexingTab_AddRoot_WhenDialogReturnsPath_AddsUnique()
    {
        FakeDialogService dialog = new() { DirectoryToReturn = @"C:\Vault" };
        IndexingTabViewModel sut = new(IndexingSettings.Default, dialog);

        sut.AddRootCommand.Execute(null);
        sut.AddRootCommand.Execute(null); // dieselbe Directory erneut -> keine Dublette

        _ = Assert.Single(sut.Roots);
        Assert.Equal(@"C:\Vault", sut.Roots[0]);
        Assert.Equal(2, dialog.PickDirectoryCalls);
    }

    [Fact]
    public void IndexingTab_AddRoot_WhenDialogCancelled_AddsNothing()
    {
        FakeDialogService dialog = new() { DirectoryToReturn = null };
        IndexingTabViewModel sut = new(IndexingSettings.Default, dialog);

        sut.AddRootCommand.Execute(null);

        Assert.Empty(sut.Roots);
    }

    [Fact]
    public void IndexingTab_RemoveRoot_RemovesSelectedAndUpdatesCanExecute()
    {
        IndexingTabViewModel sut = new(
            new IndexingSettings([@"C:\A", @"C:\B"], [], [], AutoExtractHashtags: true),
            new FakeDialogService());

        Assert.False(sut.RemoveRootCommand.CanExecute(null));
        sut.SelectedRoot = @"C:\A";
        Assert.True(sut.RemoveRootCommand.CanExecute(null));

        sut.RemoveRootCommand.Execute(null);

        _ = Assert.Single(sut.Roots);
        Assert.Equal(@"C:\B", sut.Roots[0]);
        Assert.Null(sut.SelectedRoot);
        Assert.False(sut.RemoveRootCommand.CanExecute(null));
    }

    [Fact]
    public void IndexingTab_AddExclusion_TrimsDedupesAndClearsInput()
    {
        IndexingTabViewModel sut = new(IndexingSettings.Default, new FakeDialogService())
        {
            NewExclusionPattern = "  **/tmp/**  ",
        };
        Assert.True(sut.AddExclusionCommand.CanExecute(null));

        sut.AddExclusionCommand.Execute(null);

        Assert.Contains("**/tmp/**", sut.ExclusionPatterns);
        Assert.Equal(string.Empty, sut.NewExclusionPattern);
        Assert.False(sut.AddExclusionCommand.CanExecute(null));
    }

    [Fact]
    public void IndexingTab_ToSettings_PreservesUiExcludedFolders()
    {
        IReadOnlyList<string> uiExcluded = [@"C:\Vault\private"];
        IndexingTabViewModel sut = new(
            new IndexingSettings([@"C:\Vault"], ["**/bin/**"], uiExcluded, AutoExtractHashtags: false),
            new FakeDialogService());

        IndexingSettings result = sut.ToSettings();

        Assert.Equal(uiExcluded, result.UiExcludedFolders);
        Assert.False(result.AutoExtractHashtags);
    }

    [Fact]
    public async Task SettingsWindow_ApplyAndClose_OnValidSettings_SavesAndRequestsCloseWithSaved()
    {
        FakeSettingsService settings = new(AppSettings.Default);
        SettingsWindowViewModel sut = BuildWindow(settings, new FakeDialogService());
        SettingsCloseEventArgs? closeArgs = null;
        sut.CloseRequested += (_, e) => closeArgs = e;

        await sut.ApplyAndCloseCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.NotNull(settings.SavedSettings);
        Assert.NotNull(closeArgs);
        Assert.True(closeArgs!.SavedChanges);
    }

    [Fact]
    public async Task SettingsWindow_ApplyAndClose_OnInvalidRoot_ShowsErrorAndDoesNotSave()
    {
        FakeSettingsService settings = new(AppSettings.Default);
        FakeDialogService dialog = new();
        SettingsWindowViewModel sut = BuildWindow(settings, dialog);
        sut.Indexing.Roots.Add("relative/not/qualified"); // SettingsValidator: nicht vollqualifiziert -> ungültig
        bool closed = false;
        sut.CloseRequested += (_, _) => closed = true;

        await sut.ApplyAndCloseCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Null(settings.SavedSettings);
        Assert.False(closed);
        _ = Assert.NotNull(dialog.LastError);
    }

    [Fact]
    public async Task SettingsWindow_ApplyAndClose_OnSaveFailure_ShowsError()
    {
        FakeSettingsService settings = new(AppSettings.Default) { SaveException = new IOException("disk full") };
        FakeDialogService dialog = new();
        SettingsWindowViewModel sut = BuildWindow(settings, dialog);

        await sut.ApplyAndCloseCommand.ExecuteAsync(null).ConfigureAwait(true);

        _ = Assert.NotNull(dialog.LastError);
        Assert.Equal("Speichern fehlgeschlagen", dialog.LastError!.Value.Title);
    }

    [Fact]
    public void SettingsWindow_Cancel_RequestsCloseWithoutSaved()
    {
        FakeSettingsService settings = new(AppSettings.Default);
        SettingsWindowViewModel sut = BuildWindow(settings, new FakeDialogService());
        SettingsCloseEventArgs? closeArgs = null;
        sut.CloseRequested += (_, e) => closeArgs = e;

        sut.CancelCommand.Execute(null);

        Assert.NotNull(closeArgs);
        Assert.False(closeArgs!.SavedChanges);
        Assert.Null(settings.SavedSettings);
    }

    private static SettingsWindowViewModel BuildWindow(FakeSettingsService settings, FakeDialogService dialog) =>
        new(settings, new SettingsValidator(new FakeFileSystem()), dialog, NullLogger<SettingsWindowViewModel>.Instance);
}
