using System.IO;
using System.Text;
using MdExplorer.App.Tests.Fakes;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Der Dokumentbereich lädt Vorschau, Editor und Bezüge zur selben Datei, für nicht indizierte
/// Dateien direkt von der Platte. Scheitert ein Teil, wird der ganze Bereich geleert, statt
/// Vorschau, Editor und Bezüge auf verschiedenen Dateien stehen zu lassen.
/// </summary>
public sealed class DocumentPanelViewModelLoadTests
{
    private const string TestPath = @"C:\notes\datei.md";
    private const string UnindexedPath = @"C:\notes\neu.md";
    private static readonly Guid FirstId = new("abababab-0000-0000-0000-000000000001");
    private static readonly Guid SecondId = new("abababab-0000-0000-0000-000000000002");

    [Fact]
    public async Task LoadAsync_OnExistingDocument_LoadsPreviewEditorAndRelations()
    {
        using DocumentPanelHarness harness = new();
        harness.AddIndexed(FirstId, TestPath, "# Titel", "<h1>Titel</h1>");

        _ = await harness.Sut.LoadAsync(FirstId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Contains("<h1>Titel</h1>", harness.Sut.Preview.Html, StringComparison.Ordinal);
        Assert.Equal(FirstId, harness.Sut.Preview.CurrentDocumentId);
        Assert.Equal(TestPath, harness.Editor.FilePath);
        Assert.Equal(FirstId, harness.Editor.MarkdownFileId);
        Assert.True(harness.Sut.Relations.FileActions.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task LoadByPathAsync_OnIndexedFile_GoesThroughTheIndex()
    {
        using DocumentPanelHarness harness = new();
        harness.AddIndexed(FirstId, TestPath, "# Body", "<p>Indexed</p>");

        _ = await harness.Sut.LoadByPathAsync(TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(FirstId, harness.Editor.MarkdownFileId);
        Assert.Equal(FirstId, harness.Sut.Preview.CurrentDocumentId);
        Assert.Contains("<p>Indexed</p>", harness.Sut.Preview.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadByPathAsync_OnUnindexedFile_FallsBackToDirectLoad()
    {
        using DocumentPanelHarness harness = new();
        harness.FileSystem.Files[UnindexedPath] = Encoding.UTF8.GetBytes("# Direct");
        harness.Parser.SetParseResult("<p>Direct</p>");

        _ = await harness.Sut.LoadByPathAsync(UnindexedPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Contains("<p>Direct</p>", harness.Sut.Preview.Html, StringComparison.Ordinal);
        Assert.Equal(Guid.Empty, harness.Editor.MarkdownFileId);
        Assert.Equal(UnindexedPath, harness.Editor.FilePath);
    }

    [Fact]
    public async Task LoadByPathAsync_OnUnindexedFile_DropsTheRelationsOfThePreviousDocument()
    {
        // Der Direktladeweg tauschte Vorschau und Editor, ließ die Bezüge aber stehen. Umbenennen
        // und Löschen hätten dann die vorige Datei getroffen, während die neue zu sehen war.
        using DocumentPanelHarness harness = new();
        harness.AddIndexed(FirstId, TestPath, "# Erste", "<h1>Erste</h1>");
        harness.FileSystem.Files[UnindexedPath] = Encoding.UTF8.GetBytes("# Neu");
        _ = await harness.Sut.LoadAsync(FirstId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = await harness.Sut.LoadByPathAsync(UnindexedPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(harness.Sut.Relations.ShowsNoDocument);
        Assert.False(harness.Sut.Relations.FileActions.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task LoadByPathAsync_OnUnindexedFile_PointsThePreviewAtThatFilesFolder()
    {
        using DocumentPanelHarness harness = new();
        harness.FileSystem.Files[UnindexedPath] = Encoding.UTF8.GetBytes("# Direct");

        _ = await harness.Sut.LoadByPathAsync(UnindexedPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.NotNull(harness.Sut.Preview.ImageBase);
        Assert.Equal(@"C:\notes", harness.Sut.Preview.ImageBase.Folder, StringComparer.OrdinalIgnoreCase);
        Assert.Null(harness.Sut.Preview.CurrentDocumentId);
    }

    [Fact]
    public async Task LoadByPathAsync_OnAFileThatIsGone_ParsesNothingAndClearsTheArea()
    {
        using DocumentPanelHarness harness = new();
        harness.AddIndexed(FirstId, TestPath, "# Erste", "<h1>Erste</h1>");
        _ = await harness.Sut.LoadAsync(FirstId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = await harness.Sut.LoadByPathAsync(UnindexedPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(0, harness.Parser.ParseCount);
        Assert.Null(harness.Editor.FilePath);
        Assert.Null(harness.Sut.Preview.CurrentDocumentId);
    }

    [Fact]
    public async Task LoadByPathAsync_WhenTheFileCannotBeRead_ClearsTheWholeArea()
    {
        // Ist die neue Datei gesperrt, zeigte der Bereich bisher still die vorige weiter. Wer
        // dann bearbeitet, hält die vorige für die angeklickte.
        using DocumentPanelHarness harness = new();
        harness.AddIndexed(FirstId, TestPath, "# Erste", "<h1>Erste</h1>");
        harness.FileSystem.Files[UnindexedPath] = Encoding.UTF8.GetBytes("# Neu");
        _ = await harness.Sut.LoadAsync(FirstId, TestContext.Current.CancellationToken).ConfigureAwait(true);
        harness.FileSystem.FailOnRead = new IOException("Datei ist gesperrt.");

        _ = await harness.Sut.LoadByPathAsync(UnindexedPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Null(harness.Editor.FilePath);
        Assert.Null(harness.Sut.Preview.CurrentDocumentId);
        Assert.DoesNotContain("<h1>Erste</h1>", harness.Sut.Preview.Html, StringComparison.Ordinal);
        Assert.True(harness.Sut.Relations.ShowsNoDocument);
        Assert.Equal(0, harness.Parser.ParseCount);
    }

    [Fact]
    public async Task LoadByPathAsync_WhenTheFileCannotBeRendered_ClearsTheArea()
    {
        // Der Klick auf eine Datei, die Markdig abweist, darf nicht aus dem Bedienschritt
        // herausfliegen.
        using DocumentPanelHarness harness = new();
        harness.FileSystem.Files[UnindexedPath] = Encoding.UTF8.GetBytes("# Direct");
        harness.Parser.FailOnParse = new ArgumentException("Zu tief verschachtelt.");

        _ = await harness.Sut.LoadByPathAsync(UnindexedPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Null(harness.Editor.FilePath);
        Assert.Null(harness.Sut.Preview.CurrentDocumentId);
    }

    [Fact]
    public async Task LoadAsync_WhenAnEarlierLoadAnswersLast_ShowsTheLaterDocument()
    {
        // Zwei schnelle Klicks: Die Antwort des ersten trifft nach der des zweiten ein. Die
        // Anzeige muss beim zuletzt gewählten Dokument bleiben.
        using DocumentPanelHarness harness = new();
        harness.AddIndexed(FirstId, @"C:\notes\erste.md", "# Erste", "<h1>Erste</h1>");
        harness.AddIndexed(SecondId, @"C:\notes\zweite.md", "# Zweite", "<h1>Zweite</h1>");
        TaskCompletionSource firstAnswer = harness.Locator.Hold(FirstId);

        Task<bool> first = harness.Sut.LoadAsync(FirstId, TestContext.Current.CancellationToken);
        _ = await harness.Sut.LoadAsync(SecondId, TestContext.Current.CancellationToken).ConfigureAwait(true);
        firstAnswer.SetResult();
        _ = await first.ConfigureAwait(true);

        Assert.Equal(@"C:\notes\zweite.md", harness.Editor.FilePath);
        Assert.Equal(SecondId, harness.Sut.Preview.CurrentDocumentId);
        Assert.Contains("<h1>Zweite</h1>", harness.Sut.Preview.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_WhenTheFileIsGoneFromDisk_ClearsTheEditorInsteadOfKeepingThePreviousOne()
    {
        // Steht das neue Dokument im Index, liegt aber nicht mehr auf der Platte, darf der Editor
        // nicht das vorige behalten: Wer dann bearbeitet, schriebe in die falsche Datei.
        using DocumentPanelHarness harness = new();
        harness.AddIndexed(FirstId, TestPath, "# Erste", "<h1>Erste</h1>");
        harness.AddIndexed(SecondId, @"C:\notes\weg.md", "# Weg", "<h1>Weg</h1>");
        _ = await harness.Sut.LoadAsync(FirstId, TestContext.Current.CancellationToken).ConfigureAwait(true);
        harness.FileSystem.FailOnRead = new FileNotFoundException("Datei gelöscht.");

        _ = await harness.Sut.LoadAsync(SecondId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Null(harness.Editor.FilePath);
        Assert.Equal(string.Empty, harness.Editor.Text);
    }

    [Fact]
    public async Task LoadAsync_WhenThePathLookupFails_ClearsPreviewAndEditorTogether()
    {
        // Vorschau und Editor zeigen immer dieselbe Datei oder beide keine.
        using DocumentPanelHarness harness = new();
        harness.AddIndexed(FirstId, TestPath, "# Erste", "<h1>Erste</h1>");
        harness.AddIndexed(SecondId, @"C:\notes\zweite.md", "# Zweite", "<h1>Zweite</h1>");
        _ = await harness.Sut.LoadAsync(FirstId, TestContext.Current.CancellationToken).ConfigureAwait(true);
        harness.Locator.FailOnGetPath = new TestDbException("Datenbank ist beschäftigt.");

        _ = await harness.Sut.LoadAsync(SecondId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Null(harness.Editor.FilePath);
        Assert.Null(harness.Sut.Preview.CurrentDocumentId);
        Assert.DoesNotContain("<h1>Erste</h1>", harness.Sut.Preview.Html, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Eine Datenbank-Spitze beim Nachschlagen des Pfades darf den Bereich leer lassen, aber
    /// nicht die Anwendung mitnehmen. Der Nutzer klickt in der Liste; ein Absturz an dieser
    /// Stelle wäre die teuerste denkbare Antwort auf einen Klick.
    /// </remarks>
    [Fact]
    public async Task LoadAsync_WhenTheDatabaseIsBusy_LeavesThePanelEmptyWithoutThrowing()
    {
        using DocumentPanelHarness harness = new();
        harness.Locator.FailOnGetPath = new TestDbException("Datenbank ist beschäftigt.");

        _ = await harness.Sut.LoadAsync(FirstId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Null(harness.Editor.FilePath);
    }

    /// <remarks>
    /// Ein abgebrochenes Laden ist kein Fehler. Es passiert bei jedem schnellen Durchklicken,
    /// wenn der Nutzer schneller ist als die Datenbank.
    /// </remarks>
    [Fact]
    public async Task LoadAsync_WhenCancelled_ReturnsQuietly()
    {
        using DocumentPanelHarness harness = new();
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync().ConfigureAwait(true);
        // So verhält sich der echte Auflöser an einem abgebrochenen Merker.
        harness.Locator.FailOnGetPath = new OperationCanceledException(cancelled.Token);

        _ = await harness.Sut.LoadAsync(FirstId, cancelled.Token).ConfigureAwait(true);

        Assert.Null(harness.Editor.FilePath);
    }
}
