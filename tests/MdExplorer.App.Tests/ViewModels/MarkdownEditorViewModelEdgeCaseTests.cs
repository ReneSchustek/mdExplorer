using System.IO;
using System.Text;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Prüft die Schutzmechanismen des Editors. Er schreibt Dateien des Nutzers, deshalb geht es
/// hier fast durchgehend darum, dass er in Zweifelsfällen nichts tut: kein Schreiben
/// ohne Bearbeiten-Modus, keine Tag-Änderung an einer gesperrten Datei, und eine verständliche
/// Meldung statt eines Absturzes, wenn das Schreiben scheitert.
/// </summary>
public sealed class MarkdownEditorViewModelEdgeCaseTests
{
    private const string TestPath = @"C:\notizen\datei.md";
    private static readonly Guid TestId = new("ffffffff-0000-0000-0000-000000000001");

    [Fact]
    public void EnterEditMode_WithoutAFile_StaysInReadMode()
    {
        using MarkdownEditorViewModel sut = Create(new FakeFileSystem());

        sut.EnterEditMode();

        Assert.True(sut.IsLocked);
        Assert.Null(sut.StatusMessage);
    }

    [Fact]
    public async Task ExitEditMode_ReturnsToReadMode()
    {
        FakeFileSystem fs = CreateFileSystem("alt");
        using MarkdownEditorViewModel sut = Create(fs);
        await sut.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        sut.EnterEditMode();
        Assert.False(sut.IsLocked);

        sut.ExitEditMode();

        Assert.True(sut.IsLocked);
        Assert.NotNull(sut.StatusMessage);
    }

    [Fact]
    public async Task SaveAsync_WithoutAFile_WritesNothing()
    {
        FakeFileSystem fs = new();
        using MarkdownEditorViewModel sut = Create(fs);

        await sut.SaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Empty(fs.WrittenFiles);
    }

    [Fact]
    public async Task SaveAsync_WhenWritingFails_ReportsItAndKeepsTheChanges()
    {
        // Der Text darf auf keinen Fall als gespeichert gelten, sonst geht die Arbeit
        // beim nächsten Wechsel der Datei verloren.
        FakeFileSystem fs = CreateFileSystem("alt");
        fs.FailOnWrite = new IOException("Datei ist belegt");
        using MarkdownEditorViewModel sut = Create(fs);
        await sut.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        sut.EnterEditMode();
        sut.Text = "neu";

        await sut.SaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Contains("fehlgeschlagen", sut.StatusMessage!, StringComparison.Ordinal);
        Assert.True(sut.IsDirty);
        Assert.False(sut.IsSaving);
    }

    [Fact]
    public async Task SaveAsync_WhenAccessIsDenied_ReportsItAndKeepsTheChanges()
    {
        FakeFileSystem fs = CreateFileSystem("alt");
        fs.FailOnWrite = new UnauthorizedAccessException("Kein Zugriff");
        using MarkdownEditorViewModel sut = Create(fs);
        await sut.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        sut.EnterEditMode();
        sut.Text = "neu";

        await sut.SaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Contains("fehlgeschlagen", sut.StatusMessage!, StringComparison.Ordinal);
        Assert.True(sut.IsDirty);
    }

    [Fact]
    public async Task SaveAsync_WhenTheFileNoLongerExists_WritesItAnyway()
    {
        // Nach einem Direktladen liegt die Datei nicht im Bestand. Die Prüfung auf
        // Fremdänderung muss das durchlassen, sonst lässt sich eine neue Datei nie speichern.
        FakeFileSystem fs = new();
        using MarkdownEditorViewModel sut = Create(fs);
        sut.LoadDirect(TestPath, Encoding.UTF8.GetBytes("Inhalt"));
        sut.EnterEditMode();
        sut.Text = "geänderter Text";

        await sut.SaveAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.True(fs.WrittenFiles.ContainsKey(TestPath));
        Assert.False(sut.IsDirty);
    }

    [Fact]
    public async Task AddTag_WhileLocked_ChangesNothing()
    {
        FakeFileSystem fs = CreateFileSystem("Text");
        using MarkdownEditorViewModel sut = Create(fs);
        await sut.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        sut.TagInput = "bericht";

        sut.AddTag();

        Assert.Equal("Text", sut.Text, StringComparer.Ordinal);
    }

    [Fact]
    public async Task AddTag_WithOnlyAHashSign_ChangesNothing()
    {
        FakeFileSystem fs = CreateFileSystem("Text");
        using MarkdownEditorViewModel sut = Create(fs);
        await sut.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        sut.EnterEditMode();
        sut.TagInput = "#";

        sut.AddTag();

        Assert.Equal("Text", sut.Text, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("zwei worte")]
    [InlineData("a-->b")]
    public async Task AddTag_WithAnInvalidName_CannotBeExecuted(string name)
    {
        FakeFileSystem fs = CreateFileSystem("Text");
        using MarkdownEditorViewModel sut = Create(fs);
        await sut.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        sut.EnterEditMode();

        sut.TagInput = name;

        Assert.False(sut.AddTagCommand.CanExecute(null));
    }

    [Fact]
    public async Task RemoveTag_WhileLocked_ChangesNothing()
    {
        FakeFileSystem fs = CreateFileSystem("Text mit #bericht.");
        using MarkdownEditorViewModel sut = Create(fs);
        await sut.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

        sut.RemoveTag("bericht");

        Assert.Contains("#bericht", sut.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveTag_WithoutAName_ChangesNothing()
    {
        FakeFileSystem fs = CreateFileSystem("Text mit #bericht.");
        using MarkdownEditorViewModel sut = Create(fs);
        await sut.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);
        sut.EnterEditMode();

        sut.RemoveTag("   ");

        Assert.Contains("#bericht", sut.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameTag_WhileLocked_ChangesNothing()
    {
        FakeFileSystem fs = CreateFileSystem("Text mit #bericht.");
        using MarkdownEditorViewModel sut = Create(fs);
        await sut.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

        sut.RenameTag("bericht", "notiz");

        Assert.Contains("#bericht", sut.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenameTag_WithoutNames_Throws()
    {
        using MarkdownEditorViewModel sut = Create(new FakeFileSystem());

        _ = Assert.Throws<ArgumentException>(() => sut.RenameTag("   ", "notiz"));
        _ = Assert.Throws<ArgumentException>(() => sut.RenameTag("bericht", "   "));
    }

    [Fact]
    public void LoadDirect_WithoutContent_Throws()
    {
        using MarkdownEditorViewModel sut = Create(new FakeFileSystem());

        _ = Assert.Throws<ArgumentNullException>(() => sut.LoadDirect(TestPath, null!));
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        MarkdownEditorViewModel sut = Create(new FakeFileSystem());

        sut.Dispose();
        sut.Dispose();
    }

    [Fact]
    public async Task LoadAsync_ReadsTheTagsWithoutWaiting()
    {
        // Beim Laden stehen die Schlagworte sofort da; die Wartezeit gilt nur für das Tippen.
        FakeFileSystem fs = CreateFileSystem("Text mit #bericht.");
        using MarkdownEditorViewModel sut = Create(fs);

        await sut.LoadAsync(TestId, TestPath, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal("Text mit #bericht.", sut.Text, StringComparer.Ordinal);
        Assert.Contains("bericht", sut.Tags, StringComparer.OrdinalIgnoreCase);
    }

    private static FakeFileSystem CreateFileSystem(string content)
    {
        FakeFileSystem fs = new();
        fs.Files[TestPath] = Encoding.UTF8.GetBytes(content);
        return fs;
    }

    private static MarkdownEditorViewModel Create(FakeFileSystem fs) => TestEditors.Create(fs);
}
