using System.Text;
using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Der Dokumentbereich baut die Vorschau nach dem Speichern aus dem gespeicherten Text neu und
/// fragt vor jedem Dokumentwechsel nach ungespeicherten Änderungen.
/// </summary>
public sealed class DocumentPanelViewModelEditTests
{
    private const string TestPath = @"C:\notes\datei.md";
    private const string OpenPath = @"C:\notes\offen.md";
    private const string OtherPath = @"C:\notes\anderes.md";
    private static readonly Guid FileId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OpenId = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid OtherId = new("aaaaaaaa-0000-0000-0000-000000000002");

    [Fact]
    public async Task OnEditorSaved_RefreshesPreviewFromSavedText()
    {
        using DocumentPanelHarness harness = new();
        harness.FileSystem.Files[TestPath] = Encoding.UTF8.GetBytes("alt");
        harness.Parser.SetParseResult("<p>nach Save</p>");
        await harness.Editor.LoadAsync(FileId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        harness.Editor.EnterEditMode();
        harness.Editor.Text = "neuer Editor-Text";

        await harness.Editor.SaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Contains("<p>nach Save</p>", harness.Sut.Preview.Html, StringComparison.Ordinal);
        Assert.Equal(1, harness.Parser.ParseCount);
        Assert.Equal("neuer Editor-Text", harness.Parser.LastInput);
    }

    [Fact]
    public async Task OnEditorSaved_ThenAThemeChange_KeepsShowingTheSavedText()
    {
        // Der Wechsel des Erscheinungsbilds baut die Vorschau aus dem gemerkten Inhalt neu.
        // Stammte der noch vom Laden, spränge die Vorschau auf den Stand vor dem Speichern.
        using DocumentPanelHarness harness = new();
        harness.AddIndexed(FileId, TestPath, "alt", "<p>vor Save</p>");
        harness.Parser.SetParseResult("<p>nach Save</p>");
        _ = await harness.Sut.LoadAsync(FileId, TestContext.Current.CancellationToken).ConfigureAwait(true);
        harness.Editor.EnterEditMode();
        harness.Editor.Text = "neuer Text";
        await harness.Editor.SaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        harness.Sut.Preview.RebuildForSettings();

        Assert.Contains("<p>nach Save</p>", harness.Sut.Preview.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<p>vor Save</p>", harness.Sut.Preview.Html, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Nach dem Speichern wird die Vorschau aus dem gespeicherten Text neu gebaut. Weist
    /// Markdig den Text ab (zu tief verschachtelt), darf das den Speichervorgang nicht
    /// nachträglich zum Fehler machen: Die Datei liegt zu diesem Zeitpunkt bereits auf der
    /// Platte.
    /// </remarks>
    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(NotSupportedException))]
    [InlineData(typeof(FormatException))]
    public async Task OnEditorSaved_WhenTheTextCannotBeRendered_KeepsTheSaveIntact(Type renderFailure)
    {
        using DocumentPanelHarness harness = new();
        harness.FileSystem.Files[TestPath] = Encoding.UTF8.GetBytes("alt");
        await harness.Editor.LoadAsync(FileId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        harness.Editor.EnterEditMode();
        harness.Editor.Text = "neuer Text";

        // Welche Ausnahme Markdig für einen abgewiesenen Text wirft, legt die Bibliothek fest,
        // nicht dieses Programm. Deshalb zählt hier jede erholbare Ausnahme.
        harness.Parser.FailOnParse = Assert.IsAssignableFrom<Exception>(Activator.CreateInstance(renderFailure, "Zu tief verschachtelt."));
        await harness.Editor.SaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal("neuer Text", Encoding.UTF8.GetString(harness.FileSystem.WrittenFiles[TestPath]), StringComparer.Ordinal);
    }

    [Fact]
    public void ToggleMode_FlipsBetweenReadAndEdit()
    {
        using DocumentPanelHarness harness = new();
        DocumentPanelViewModel sut = harness.Sut;

        Assert.Equal(DocumentPanelMode.Read, sut.Mode);
        Assert.True(sut.IsReadMode);

        sut.ToggleMode();
        Assert.Equal(DocumentPanelMode.Edit, sut.Mode);
        Assert.True(sut.IsEditMode);

        sut.ToggleMode();
        Assert.Equal(DocumentPanelMode.Read, sut.Mode);
        Assert.True(sut.IsReadMode);
    }

    [Fact]
    public void IsEditMode_Set_SwitchesTheMode()
    {
        // Darauf bindet der Schalter „Bearbeiten“ in beide Richtungen; über die Bedienungshilfen
        // setzt er nur diesen Wert.
        using DocumentPanelHarness harness = new();
        DocumentPanelViewModel sut = harness.Sut;
        List<string?> changed = [];
        sut.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        sut.IsEditMode = true;
        Assert.Equal(DocumentPanelMode.Edit, sut.Mode);
        Assert.Contains(nameof(DocumentPanelViewModel.IsEditMode), changed);

        sut.IsEditMode = false;
        Assert.Equal(DocumentPanelMode.Read, sut.Mode);
        Assert.True(sut.IsReadMode);
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromSaved()
    {
        using DocumentPanelHarness harness = new();
        harness.FileSystem.Files[TestPath] = Encoding.UTF8.GetBytes("alt");
        await harness.Editor.LoadAsync(FileId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        harness.Editor.EnterEditMode();
        harness.Editor.Text = "geändert";
        await harness.Editor.SaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        int parseCountAfterFirstSave = harness.Parser.ParseCount;

        harness.Sut.Dispose();
        harness.Sut.Dispose(); // idempotent

        // Nach Dispose ist Editor.Saved nicht mehr abonniert; ein weiterer Save darf die
        // Vorschau nicht mehr neu bauen.
        harness.Editor.EnterEditMode();
        harness.Editor.Text = "nochmal geändert";
        await harness.Editor.SaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(parseCountAfterFirstSave, harness.Parser.ParseCount);
    }

    [Fact]
    public async Task LoadAsync_WithUnsavedChangesAndCancel_KeepsTheOpenDocument()
    {
        // Ohne die Frage überschrieb das Laden den Editor, und die getippte Arbeit war weg.
        using DocumentPanelHarness harness = await WithUnsavedChangesAsync(UnsavedChangesDecision.Cancel).ConfigureAwait(true);

        bool loaded = await harness.Sut.LoadAsync(OtherId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(loaded);
        Assert.Equal(OpenPath, harness.Editor.FilePath);
        Assert.Equal("# Offen ergänzt", harness.Editor.Text, StringComparer.Ordinal);
        Assert.True(harness.Editor.IsDirty);
        Assert.Equal(OpenId, harness.Sut.Preview.CurrentDocumentId);
    }

    [Fact]
    public async Task LoadByPathAsync_WithUnsavedChangesAndCancel_KeepsTheOpenDocument()
    {
        using DocumentPanelHarness harness = await WithUnsavedChangesAsync(UnsavedChangesDecision.Cancel).ConfigureAwait(true);

        bool loaded = await harness.Sut.LoadByPathAsync(OtherPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(loaded);
        Assert.Equal(OpenPath, harness.Editor.FilePath);
        Assert.Equal("# Offen ergänzt", harness.Editor.Text, StringComparer.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_WithUnsavedChangesAndDiscard_LoadsTheNextDocument()
    {
        using DocumentPanelHarness harness = await WithUnsavedChangesAsync(UnsavedChangesDecision.Discard).ConfigureAwait(true);

        bool loaded = await harness.Sut.LoadAsync(OtherId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(loaded);
        Assert.Equal(OtherPath, harness.Editor.FilePath);
        Assert.Equal("# Anderes", harness.Editor.Text, StringComparer.Ordinal);
        Assert.False(harness.Editor.IsDirty);
        Assert.Empty(harness.FileSystem.WrittenFiles);
    }

    [Fact]
    public async Task LoadAsync_WithUnsavedChangesAndSave_StoresThemBeforeLoading()
    {
        using DocumentPanelHarness harness = await WithUnsavedChangesAsync(UnsavedChangesDecision.Save).ConfigureAwait(true);

        bool loaded = await harness.Sut.LoadAsync(OtherId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(loaded);
        Assert.Equal("# Offen ergänzt", Encoding.UTF8.GetString(harness.FileSystem.WrittenFiles[OpenPath]), StringComparer.Ordinal);
        Assert.Equal(OtherPath, harness.Editor.FilePath);
    }

    /// <summary>Ein geöffnetes Dokument mit getippten Änderungen und ein zweites zum Wechseln.</summary>
    private static async Task<DocumentPanelHarness> WithUnsavedChangesAsync(UnsavedChangesDecision decision)
    {
        DocumentPanelHarness harness = new(new FakeUnsavedChangesDialogService { Decision = decision });
        harness.AddIndexed(OpenId, OpenPath, "# Offen", "<h1>Offen</h1>");
        harness.AddIndexed(OtherId, OtherPath, "# Anderes", "<h1>Anderes</h1>");
        _ = await harness.Sut.LoadAsync(OpenId, TestContext.Current.CancellationToken).ConfigureAwait(true);
        harness.Editor.EnterEditMode();
        harness.Editor.Text = "# Offen ergänzt";
        return harness;
    }
}
