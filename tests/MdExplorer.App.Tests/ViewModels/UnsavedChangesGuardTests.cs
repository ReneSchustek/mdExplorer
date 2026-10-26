using System.IO;
using System.Text;
using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Models;
using MdExplorer.Parser.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Getippte Arbeit geht nicht still verloren: Bevor das Dokument den Editor verlässt, entscheidet
/// der Nutzer zwischen Speichern, Verwerfen und Abbrechen, und jede Antwort tut genau das.
/// </summary>
public sealed class UnsavedChangesGuardTests
{
    private const string FilePath = @"C:\notizen\entwurf.md";
    private static readonly Guid FileId = new("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task ConfirmLeaveAsync_WithoutChanges_LetsGoWithoutAsking()
    {
        Fixture fixture = await Fixture.CreateAsync().ConfigureAwait(true);

        bool allowed = await fixture.Guard.ConfirmLeaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(allowed);
        Assert.Equal(0, fixture.Dialog.AskCount);
    }

    [Fact]
    public async Task ConfirmLeaveAsync_OnCancel_KeepsTheChangesAndStays()
    {
        Fixture fixture = await Fixture.CreateDirtyAsync(UnsavedChangesDecision.Cancel).ConfigureAwait(true);

        bool allowed = await fixture.Guard.ConfirmLeaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(allowed);
        Assert.Equal("# Entwurf ergänzt", fixture.Editor.Text, StringComparer.Ordinal);
        Assert.True(fixture.Editor.IsDirty);
        Assert.Empty(fixture.FileSystem.WrittenFiles);
    }

    [Fact]
    public async Task ConfirmLeaveAsync_OnDiscard_RestoresTheLoadedText()
    {
        Fixture fixture = await Fixture.CreateDirtyAsync(UnsavedChangesDecision.Discard).ConfigureAwait(true);

        bool allowed = await fixture.Guard.ConfirmLeaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(allowed);
        Assert.Equal("# Entwurf", fixture.Editor.Text, StringComparer.Ordinal);
        Assert.False(fixture.Editor.IsDirty);
        Assert.Empty(fixture.FileSystem.WrittenFiles);
    }

    [Fact]
    public async Task ConfirmLeaveAsync_OnSave_WritesTheFileWithoutAskingASecondTime()
    {
        // Der Editor fragt beim Speichern sonst „wirklich speichern?“; hier lehnt diese Frage ab.
        // Gespeichert wird trotzdem, denn der Nutzer hat „Speichern“ schon gewählt.
        Fixture fixture = await Fixture.CreateDirtyAsync(UnsavedChangesDecision.Save).ConfigureAwait(true);

        bool allowed = await fixture.Guard.ConfirmLeaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(allowed);
        Assert.False(fixture.Editor.IsDirty);
        Assert.Equal("# Entwurf ergänzt", Encoding.UTF8.GetString(fixture.FileSystem.WrittenFiles[FilePath]), StringComparer.Ordinal);
    }

    [Fact]
    public async Task ConfirmLeaveAsync_WhenSavingFails_Stays()
    {
        Fixture fixture = await Fixture.CreateDirtyAsync(UnsavedChangesDecision.Save).ConfigureAwait(true);
        fixture.FileSystem.FailOnWrite = new IOException("Datei schreibgeschützt");

        bool allowed = await fixture.Guard.ConfirmLeaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(allowed);
        Assert.True(fixture.Editor.IsDirty);
        Assert.Equal("# Entwurf ergänzt", fixture.Editor.Text, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ConfirmLeaveAsync_NamesTheFileInTheQuestion()
    {
        Fixture fixture = await Fixture.CreateDirtyAsync(UnsavedChangesDecision.Cancel).ConfigureAwait(true);

        _ = await fixture.Guard.ConfirmLeaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal("entwurf.md", fixture.Dialog.LastDocumentName, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ConfirmLeaveAsync_WhileTheQuestionIsOpen_KeepsTheChangesForTheFirstAnswer()
    {
        // Ein Doppelklick im Explorer erreicht das Programm, während der modale Dialog offen
        // steht. Die zweite Rückfrage darf die Änderungen nicht verwerfen, bevor der Nutzer auf
        // die erste „Speichern“ geantwortet hat.
        Fixture fixture = await Fixture.CreateDirtyAsync(UnsavedChangesDecision.Save).ConfigureAwait(true);
        Task<bool>? nested = null;
        fixture.Dialog.WhileOpen = () =>
        {
            fixture.Dialog.WhileOpen = null;
            fixture.Dialog.Decision = UnsavedChangesDecision.Discard;
            nested = fixture.Guard.ConfirmLeaveAsync(TestContext.Current.CancellationToken);
            fixture.Dialog.Decision = UnsavedChangesDecision.Save;
        };

        bool allowed = await fixture.Guard.ConfirmLeaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.NotNull(nested);
        Assert.False(await nested.ConfigureAwait(true));
        Assert.Equal(1, fixture.Dialog.AskCount);
        Assert.True(allowed);
        Assert.Equal("# Entwurf ergänzt", Encoding.UTF8.GetString(fixture.FileSystem.WrittenFiles[FilePath]), StringComparer.Ordinal);
    }

    [Fact]
    public async Task ConfirmLeaveAsync_AfterAnAnsweredQuestion_AsksAgain()
    {
        Fixture fixture = await Fixture.CreateDirtyAsync(UnsavedChangesDecision.Cancel).ConfigureAwait(true);

        _ = await fixture.Guard.ConfirmLeaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        _ = await fixture.Guard.ConfirmLeaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(2, fixture.Dialog.AskCount);
    }

    [Fact]
    public async Task HasUnsavedChanges_FollowsTheEditor()
    {
        Fixture fixture = await Fixture.CreateAsync().ConfigureAwait(true);
        Assert.False(fixture.Guard.HasUnsavedChanges);

        fixture.Editor.EnterEditMode();
        fixture.Editor.Text = "# geändert";

        Assert.True(fixture.Guard.HasUnsavedChanges);
    }

    /// <summary>Editor mit geladener Datei, Dialog mit fester Antwort und der Wächter dazwischen.</summary>
    private sealed class Fixture
    {
        private Fixture(FakeFileSystem fileSystem, MarkdownEditorViewModel editor, FakeUnsavedChangesDialogService dialog)
        {
            FileSystem = fileSystem;
            Editor = editor;
            Dialog = dialog;
            Guard = new UnsavedChangesGuard(editor, dialog);
        }

        public FakeFileSystem FileSystem { get; }

        public MarkdownEditorViewModel Editor { get; }

        public FakeUnsavedChangesDialogService Dialog { get; }

        public UnsavedChangesGuard Guard { get; }

        public static async Task<Fixture> CreateAsync()
        {
            FakeFileSystem fileSystem = new();
            fileSystem.Files[FilePath] = Encoding.UTF8.GetBytes("# Entwurf");
            // Die Rückfrage „wirklich speichern?“ lehnt ab: Der Wächter darf sie nicht stellen.
            MarkdownEditorViewModel editor = TestEditors.Create(fileSystem, confirmation: new FakeEditorConfirmation { Result = false });
            await editor.LoadAsync(FileId, FilePath, TestContext.Current.CancellationToken).ConfigureAwait(true);
            return new Fixture(fileSystem, editor, new FakeUnsavedChangesDialogService());
        }

        public static async Task<Fixture> CreateDirtyAsync(UnsavedChangesDecision decision)
        {
            Fixture fixture = await CreateAsync().ConfigureAwait(true);
            fixture.Dialog.Decision = decision;
            fixture.Editor.EnterEditMode();
            fixture.Editor.Text = "# Entwurf ergänzt";
            return fixture;
        }
    }
}
