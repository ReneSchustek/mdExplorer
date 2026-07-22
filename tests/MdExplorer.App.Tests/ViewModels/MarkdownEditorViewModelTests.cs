using System.Text;
using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Parser.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Tests fuer Dirty-Tracking, Live-Tag-Extraktion und Save-Pfad des Markdown-Editor-ViewModels.
/// </summary>
public sealed class MarkdownEditorViewModelTests
{
    private const string TestPath = @"C:\notes\datei.md";
    private static readonly TimeSpan TestDebounce = TimeSpan.FromMilliseconds(40);

    [Fact]
    public async Task LoadAsync_SetztTextUndResetIsDirty()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("# Titel\r\nText");
        using MarkdownEditorViewModel vm = CreateViewModel(fs, TimeSpan.Zero);

        await vm.LoadAsync(Guid.NewGuid(), TestPath, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("# Titel\r\nText", vm.Text);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task TextAenderung_MarkiertIsDirty()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("alt");
        using MarkdownEditorViewModel vm = CreateViewModel(fs, TimeSpan.Zero);
        await vm.LoadAsync(Guid.NewGuid(), TestPath, CancellationToken.None).ConfigureAwait(true);

        vm.Text = "neu";

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public async Task TextZurueckaufOriginal_LoeschtIsDirty()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("original");
        using MarkdownEditorViewModel vm = CreateViewModel(fs, TimeSpan.Zero);
        await vm.LoadAsync(Guid.NewGuid(), TestPath, CancellationToken.None).ConfigureAwait(true);
        vm.Text = "geaendert";
        Assert.True(vm.IsDirty);

        vm.Text = "original";

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task LiveTagExtraktion_LiefertTagsBeiTextAenderung()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("Body");
        using MarkdownEditorViewModel vm = CreateViewModel(fs, TestDebounce);
        await vm.LoadAsync(Guid.NewGuid(), TestPath, CancellationToken.None).ConfigureAwait(true);
        TaskCompletionSource completion = new();
        vm.TagsRefreshed += (_, _) => completion.TrySetResult();

        vm.Text = "Body mit #alpha und #beta";
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(true);

        Assert.Contains("alpha", vm.Tags);
        Assert.Contains("beta", vm.Tags);
    }

    [Fact]
    public async Task SaveAsync_PersistiertAtomarUndResetIsDirty()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("alt\r\n");
        using MarkdownEditorViewModel vm = CreateViewModel(fs, TimeSpan.Zero);
        await vm.LoadAsync(Guid.NewGuid(), TestPath, CancellationToken.None).ConfigureAwait(true);
        vm.EnterEditMode();
        vm.Text = "neuer Inhalt";

        await vm.SaveAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.True(fs.WrittenFiles.ContainsKey(TestPath));
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task SaveAsync_NormalisertEolAufOriginal()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("zeile1\nzeile2\n");
        using MarkdownEditorViewModel vm = CreateViewModel(fs, TimeSpan.Zero);
        await vm.LoadAsync(Guid.NewGuid(), TestPath, CancellationToken.None).ConfigureAwait(true);
        vm.EnterEditMode();
        vm.Text = "zeile1\r\nzeile2\r\nzeile3\r\n";

        await vm.SaveAsync(CancellationToken.None).ConfigureAwait(true);

        string written = Encoding.UTF8.GetString(fs.WrittenFiles[TestPath]);
        Assert.Equal("zeile1\nzeile2\nzeile3\n", written);
    }

    [Fact]
    public async Task SaveAsync_BeiExternerAenderung_StatusMeldetKonflikt()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("v1");
        using MarkdownEditorViewModel vm = CreateViewModel(fs, TimeSpan.Zero);
        await vm.LoadAsync(Guid.NewGuid(), TestPath, CancellationToken.None).ConfigureAwait(true);
        vm.EnterEditMode();
        vm.Text = "v2 vom Editor";
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("v1-extern-veraendert");

        await vm.SaveAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.False(fs.WrittenFiles.ContainsKey(TestPath));
        Assert.True(vm.IsDirty);
        Assert.Contains("extern", vm.StatusMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddTag_LeerenText_FuegtVerwaltetenBlockAn()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("Text");
        using MarkdownEditorViewModel vm = CreateViewModel(fs, TimeSpan.Zero);
        await vm.LoadAsync(Guid.NewGuid(), TestPath, CancellationToken.None).ConfigureAwait(true);
        vm.EnterEditMode();
        vm.TagInput = "wichtig";

        vm.AddTag();

        Assert.Contains("#wichtig", vm.Text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, vm.TagInput);
    }

    [Fact]
    public async Task RemoveTag_EntferntTagAusText()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("Text mit #weg und #bleibt.");
        using MarkdownEditorViewModel vm = CreateViewModel(fs, TimeSpan.Zero);
        await vm.LoadAsync(Guid.NewGuid(), TestPath, CancellationToken.None).ConfigureAwait(true);
        vm.EnterEditMode();

        vm.RemoveTag("weg");

        Assert.DoesNotContain("#weg", vm.Text, StringComparison.Ordinal);
        Assert.Contains("#bleibt", vm.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameTag_BenenntAlleVorkommenUm()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("#alpha hier, #alpha dort.");
        using MarkdownEditorViewModel vm = CreateViewModel(fs, TimeSpan.Zero);
        await vm.LoadAsync(Guid.NewGuid(), TestPath, CancellationToken.None).ConfigureAwait(true);
        vm.EnterEditMode();

        vm.RenameTag("alpha", "omega");

        Assert.DoesNotContain("#alpha", vm.Text, StringComparison.Ordinal);
        Assert.Contains("#omega", vm.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAsync_LoestSavedEvent()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("alt");
        using MarkdownEditorViewModel vm = CreateViewModel(fs, TimeSpan.Zero);
        await vm.LoadAsync(Guid.NewGuid(), TestPath, CancellationToken.None).ConfigureAwait(true);
        vm.EnterEditMode();
        vm.Text = "neu";
        string? receivedText = null;
        vm.Saved += (_, args) => receivedText = args.SavedText;

        await vm.SaveAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("neu", receivedText);
    }

    [Fact]
    public async Task LoadAsync_SetztIsLockedAufTrue()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("Body");
        using MarkdownEditorViewModel vm = CreateViewModel(fs, TimeSpan.Zero);

        await vm.LoadAsync(Guid.NewGuid(), TestPath, CancellationToken.None).ConfigureAwait(true);

        Assert.True(vm.IsLocked);
    }

    [Fact]
    public async Task SaveAsync_OnLockedFile_DoesNotWrite()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("alt");
        using MarkdownEditorViewModel vm = CreateViewModel(fs, TimeSpan.Zero);
        await vm.LoadAsync(Guid.NewGuid(), TestPath, CancellationToken.None).ConfigureAwait(true);

        await vm.SaveAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.False(fs.WrittenFiles.ContainsKey(TestPath));
        Assert.True(vm.IsLocked);
    }

    [Fact]
    public async Task LoadDirectAsync_VerwendetUebergebenenTextUndSetztIsLockedTrue()
    {
        FakeFileSystem fs = new();
        using MarkdownEditorViewModel vm = CreateViewModel(fs, TimeSpan.Zero);

        await vm.LoadDirectAsync(TestPath, "# Headline\nText", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("# Headline\nText", vm.Text);
        Assert.Equal(Guid.Empty, vm.MarkdownFileId);
        Assert.Equal(TestPath, vm.FilePath);
        Assert.True(vm.IsLocked);
    }

    private static MarkdownEditorViewModel CreateViewModel(FakeFileSystem fs, TimeSpan debounce)
    {
        TagExtractor extractor = new(new StubSettingsService());
        return new MarkdownEditorViewModel(
            fs,
            extractor,
            TimeProvider.System,
            NullLogger<MarkdownEditorViewModel>.Instance,
            debounce);
    }

    private static MarkdownEditorViewModel CreateViewModelWithConfirm(FakeFileSystem fs, IEditorConfirmationDialogService confirm)
    {
        TagExtractor extractor = new(new StubSettingsService());
        return new MarkdownEditorViewModel(
            fs,
            extractor,
            TimeProvider.System,
            NullLogger<MarkdownEditorViewModel>.Instance,
            TimeSpan.Zero,
            confirm);
    }

    // Save mit Confirm-Dialog.
    [Fact]
    public async Task SaveAsync_OnConfirmFalse_DoesNotPersist()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("alt");
        FakeConfirm confirm = new() { Result = false };
        using MarkdownEditorViewModel vm = CreateViewModelWithConfirm(fs, confirm);
        await vm.LoadAsync(Guid.NewGuid(), TestPath, CancellationToken.None).ConfigureAwait(true);
        vm.EnterEditMode();
        vm.Text = "neu";

        await vm.SaveAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.False(fs.WrittenFiles.ContainsKey(TestPath));
        Assert.True(vm.IsDirty, "Text bleibt dirty, weil nichts persistiert wurde.");
        Assert.Equal(1, confirm.CallCount);
    }

    [Fact]
    public async Task SaveAsync_OnConfirmTrue_PersistsAndFiresSavedEvent()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("alt");
        FakeConfirm confirm = new() { Result = true };
        using MarkdownEditorViewModel vm = CreateViewModelWithConfirm(fs, confirm);
        await vm.LoadAsync(Guid.NewGuid(), TestPath, CancellationToken.None).ConfigureAwait(true);
        vm.EnterEditMode();
        vm.Text = "neuer Inhalt";
        EditorSavedEventArgs? savedArgs = null;
        vm.Saved += (_, e) => savedArgs = e;

        await vm.SaveAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.True(fs.WrittenFiles.ContainsKey(TestPath));
        Assert.NotNull(savedArgs);
        Assert.Equal("neuer Inhalt", savedArgs!.SavedText);
        Assert.False(vm.IsDirty);
        Assert.Equal(1, confirm.CallCount);
    }

    [Fact]
    public async Task SaveAsync_InReadOnlyMode_DoesNotPromptConfirm()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("alt");
        FakeConfirm confirm = new() { Result = true };
        using MarkdownEditorViewModel vm = CreateViewModelWithConfirm(fs, confirm);
        await vm.LoadAsync(Guid.NewGuid(), TestPath, CancellationToken.None).ConfigureAwait(true);
        // Kein EnterEditMode → Editor bleibt im Anzeigen-Modus.

        await vm.SaveAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.False(fs.WrittenFiles.ContainsKey(TestPath));
        Assert.Equal(0, confirm.CallCount);
        Assert.True(vm.IsLocked);
    }

    private sealed class FakeConfirm : IEditorConfirmationDialogService
    {
        public bool Result { get; set; } = true;
        public int CallCount { get; private set; }

        public bool ConfirmSave()
        {
            CallCount++;
            return Result;
        }
    }

    private sealed class StubSettingsService : ISettingsService
    {
        public AppSettings Current { get; private set; } = AppSettings.Default;
        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(settings);
            AppSettings previous = Current;
            Current = settings;
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(previous, settings));
            return Task.CompletedTask;
        }
    }
}
