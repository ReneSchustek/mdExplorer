using System.Text;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Der Editor merkt sich, ob ungespeicherte Änderungen vorliegen, erkennt Schlagwörter beim
/// Tippen und schreibt nur im Bearbeiten-Modus, mit dem ursprünglichen Zeilenende und der
/// ursprünglichen Byte-Order-Mark und nicht über eine Datei, die inzwischen von außen geändert
/// wurde.
/// </summary>
public sealed class MarkdownEditorViewModelTests
{
    private const string TestPath = @"C:\notes\datei.md";
    private const string OtherPath = @"C:\notes\andere.md";
    private static readonly Guid TestId = new("eeeeeeee-0000-0000-0000-000000000001");
    private static readonly Guid OtherId = new("eeeeeeee-0000-0000-0000-000000000002");

    [Fact]
    public async Task LoadAsync_SetsTextAndResetsIsDirty()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("# Titel\r\nText");
        using MarkdownEditorViewModel vm = TestEditors.Create(fs);

        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal("# Titel\r\nText", vm.Text);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task LoadAsync_LetsTheEditButtonsReevaluate()
    {
        // Die Schaltflächen fragen CanExecute nur nach einem CanExecuteChanged erneut. Ohne die
        // Meldung blieb „Bearbeiten“ nach dem Laden ausgegraut: Freigegeben wird es erst, wenn
        // eine Datei geladen ist, und das Gesperrt-Kennzeichen ändert sich beim Laden nicht.
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("Text");
        using MarkdownEditorViewModel vm = TestEditors.Create(fs);
        int enterEditNotifications = 0;
        vm.EnterEditModeCommand.CanExecuteChanged += (_, _) => enterEditNotifications++;

        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(vm.EnterEditModeCommand.CanExecute(null));
        Assert.True(enterEditNotifications > 0);
    }

    [Fact]
    public async Task LoadAsync_WhenCancelledWhileReading_KeepsTheOpenDocument()
    {
        // Ein neuerer Klick bricht das Laden ab. Kommt die Datei danach doch noch an, darf sie
        // den Editor nicht mehr überschreiben.
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("offen");
        fs.Files[OtherPath] = Encoding.UTF8.GetBytes("spät");
        using MarkdownEditorViewModel vm = TestEditors.Create(fs);
        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync().ConfigureAwait(true);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => vm.LoadAsync(OtherId, OtherPath, cancelled.Token)).ConfigureAwait(true);

        Assert.Equal(TestPath, vm.FilePath);
        Assert.Equal("offen", vm.Text, StringComparer.Ordinal);
    }

    [Fact]
    public async Task TextChange_MarksIsDirty()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("alt");
        using MarkdownEditorViewModel vm = TestEditors.Create(fs);
        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

        vm.Text = "neu";

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public async Task TextBackToOriginal_ClearsIsDirty()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("original");
        using MarkdownEditorViewModel vm = TestEditors.Create(fs);
        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        vm.Text = "geändert";
        Assert.True(vm.IsDirty);

        vm.Text = "original";

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task TextChange_AfterTheWaitingTime_RefreshesTheTags()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("Body");
        FakeTimeProvider time = new(TestEditors.Start);
        using MarkdownEditorViewModel vm = TestEditors.Create(fs, time);
        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

        vm.Text = "Body mit #alpha und #beta";
        Assert.Empty(vm.Tags);
        time.Advance(DebouncedTagReader.Debounce);

        Assert.Contains("alpha", vm.Tags);
        Assert.Contains("beta", vm.Tags);
    }

    [Fact]
    public async Task TextChange_ThenADocumentSwitchBeforeTheWaitingTimeEnds_KeepsTheTagsOfTheNewDocument()
    {
        // Ein Zeitgeber, der nach dem Wechsel feuert, trüge die Schlagworte des vorigen Dokuments
        // neben das neue.
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("Body");
        fs.Files[OtherPath] = Encoding.UTF8.GetBytes("Anderes mit #gamma");
        FakeTimeProvider time = new(TestEditors.Start);
        using MarkdownEditorViewModel vm = TestEditors.Create(fs, time);
        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        vm.Text = "Body mit #alpha";

        await vm.LoadAsync(OtherId, OtherPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        time.Advance(DebouncedTagReader.Debounce);

        Assert.Equal(["gamma"], vm.Tags);
    }

    [Fact]
    public async Task SaveAsync_PersistsAtomicallyAndResetsIsDirty()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("alt\r\n");
        using MarkdownEditorViewModel vm = TestEditors.Create(fs);
        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        vm.EnterEditMode();
        vm.Text = "neuer Inhalt";

        await vm.SaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(fs.WrittenFiles.ContainsKey(TestPath));
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task SaveAsync_NormalizesEolToOriginal()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("zeile1\nzeile2\n");
        using MarkdownEditorViewModel vm = TestEditors.Create(fs);
        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        vm.EnterEditMode();
        vm.Text = "zeile1\r\nzeile2\r\nzeile3\r\n";

        await vm.SaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        string written = Encoding.UTF8.GetString(fs.WrittenFiles[TestPath]);
        Assert.Equal("zeile1\nzeile2\nzeile3\n", written);
    }

    [Fact]
    public async Task SaveAsync_OnAFileWithByteOrderMark_KeepsTheMark()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("alt")];
        using MarkdownEditorViewModel vm = TestEditors.Create(fs);
        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        vm.EnterEditMode();
        vm.Text = "neu";

        await vm.SaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal([.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("neu")], fs.WrittenFiles[TestPath]);
    }

    [Fact]
    public async Task SaveAsync_OnAFileWithoutByteOrderMark_WritesNone()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("alt");
        using MarkdownEditorViewModel vm = TestEditors.Create(fs);
        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        vm.EnterEditMode();
        vm.Text = "neu";

        await vm.SaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(Encoding.UTF8.GetBytes("neu"), fs.WrittenFiles[TestPath]);
    }

    [Fact]
    public async Task SaveAsync_OnExternalChange_StatusReportsConflict()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("v1");
        using MarkdownEditorViewModel vm = TestEditors.Create(fs);
        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        vm.EnterEditMode();
        vm.Text = "v2 vom Editor";
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("v1-extern-verändert");

        await vm.SaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(fs.WrittenFiles.ContainsKey(TestPath));
        Assert.True(vm.IsDirty);
        Assert.Contains("extern", vm.StatusMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAsync_AfterADirectLoadOfAFileWithByteOrderMark_SeesNoExternalChange()
    {
        // Aus dem Text neu kodiert fehlte die Markierung; die Prüfung hielte die unveränderte
        // Datei dann für fremd geändert, und Speichern ginge nie.
        byte[] content = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("direkt")];
        FakeFileSystem fs = new();
        fs.Files[TestPath] = content;
        using MarkdownEditorViewModel vm = TestEditors.Create(fs);
        vm.LoadDirect(TestPath, content);
        vm.EnterEditMode();
        vm.Text = "geändert";

        await vm.SaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(fs.WrittenFiles.ContainsKey(TestPath));
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task AddTag_EmptyText_AppendsManagedBlock()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("Text");
        using MarkdownEditorViewModel vm = TestEditors.Create(fs);
        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        vm.EnterEditMode();
        vm.TagInput = "wichtig";

        vm.AddTag();

        Assert.Contains("#wichtig", vm.Text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, vm.TagInput);
    }

    [Fact]
    public async Task RemoveTag_RemovesTagFromText()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("Text mit #weg und #bleibt.");
        using MarkdownEditorViewModel vm = TestEditors.Create(fs);
        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        vm.EnterEditMode();

        vm.RemoveTag("weg");

        Assert.DoesNotContain("#weg", vm.Text, StringComparison.Ordinal);
        Assert.Contains("#bleibt", vm.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameTag_RenamesAllOccurrences()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("#alpha hier, #alpha dort.");
        using MarkdownEditorViewModel vm = TestEditors.Create(fs);
        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        vm.EnterEditMode();

        vm.RenameTag("alpha", "omega");

        Assert.DoesNotContain("#alpha", vm.Text, StringComparison.Ordinal);
        Assert.Contains("#omega", vm.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAsync_RaisesSavedEventWithTheWrittenText()
    {
        // Gemeldet wird der Text, wie er auf der Platte steht, mit dem Zeilenende der Datei.
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("alt\n");
        using MarkdownEditorViewModel vm = TestEditors.Create(fs);
        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        vm.EnterEditMode();
        vm.Text = "neu\r\nzeile\r\n";
        string? receivedText = null;
        vm.Saved += (_, args) => receivedText = args.SavedText;

        await vm.SaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal("neu\nzeile\n", receivedText);
    }

    [Fact]
    public async Task LoadAsync_SetsIsLockedToTrue()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("Body");
        using MarkdownEditorViewModel vm = TestEditors.Create(fs);

        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(vm.IsLocked);
    }

    [Fact]
    public async Task SaveAsync_OnLockedFile_DoesNotWrite()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("alt");
        using MarkdownEditorViewModel vm = TestEditors.Create(fs);
        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

        await vm.SaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(fs.WrittenFiles.ContainsKey(TestPath));
        Assert.True(vm.IsLocked);
    }

    [Fact]
    public void LoadDirect_UsesTheGivenContentAndSetsIsLockedTrue()
    {
        FakeFileSystem fs = new();
        using MarkdownEditorViewModel vm = TestEditors.Create(fs);

        vm.LoadDirect(TestPath, Encoding.UTF8.GetBytes("# Headline\nText"));

        Assert.Equal("# Headline\nText", vm.Text);
        Assert.Equal(Guid.Empty, vm.MarkdownFileId);
        Assert.Equal(TestPath, vm.FilePath);
        Assert.True(vm.IsLocked);
    }

    [Fact]
    public async Task Clear_ForgetsTheDocumentAndLocksTheEditor()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("Text mit #alpha");
        using MarkdownEditorViewModel vm = TestEditors.Create(fs);
        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        vm.EnterEditMode();
        vm.Text = "geändert";

        vm.Clear();

        Assert.Null(vm.FilePath);
        Assert.Equal(Guid.Empty, vm.MarkdownFileId);
        Assert.Equal(string.Empty, vm.Text);
        Assert.False(vm.IsDirty);
        Assert.True(vm.IsLocked);
        Assert.Empty(vm.Tags);
        Assert.False(vm.EnterEditModeCommand.CanExecute(null));
    }

    [Fact]
    public async Task SaveAsync_OnConfirmFalse_DoesNotPersist()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("alt");
        FakeEditorConfirmation confirm = new() { Result = false };
        using MarkdownEditorViewModel vm = TestEditors.Create(fs, confirmation: confirm);
        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        vm.EnterEditMode();
        vm.Text = "neu";

        await vm.SaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(fs.WrittenFiles.ContainsKey(TestPath));
        Assert.True(vm.IsDirty, "Text bleibt dirty, weil nichts persistiert wurde.");
        Assert.Equal(1, confirm.CallCount);
    }

    [Fact]
    public async Task SaveAsync_OnConfirmTrue_PersistsAndFiresSavedEvent()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("alt");
        FakeEditorConfirmation confirm = new() { Result = true };
        using MarkdownEditorViewModel vm = TestEditors.Create(fs, confirmation: confirm);
        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        vm.EnterEditMode();
        vm.Text = "neuer Inhalt";
        EditorSavedEventArgs? savedArgs = null;
        vm.Saved += (_, e) => savedArgs = e;

        await vm.SaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(fs.WrittenFiles.ContainsKey(TestPath));
        Assert.NotNull(savedArgs);
        Assert.Equal("neuer Inhalt", savedArgs.SavedText);
        Assert.False(vm.IsDirty);
        Assert.Equal(1, confirm.CallCount);
    }

    [Fact]
    public async Task SaveAsync_InReadOnlyMode_DoesNotPromptConfirm()
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes("alt");
        FakeEditorConfirmation confirm = new() { Result = true };
        using MarkdownEditorViewModel vm = TestEditors.Create(fs, confirmation: confirm);
        await vm.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        // Kein EnterEditMode → Editor bleibt im Anzeigen-Modus.

        await vm.SaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(fs.WrittenFiles.ContainsKey(TestPath));
        Assert.Equal(0, confirm.CallCount);
        Assert.True(vm.IsLocked);
    }
}
