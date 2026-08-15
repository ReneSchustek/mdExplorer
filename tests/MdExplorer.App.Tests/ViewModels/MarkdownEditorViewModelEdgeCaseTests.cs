using System.IO;
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
/// Prüft die Schutzmechanismen des Editors. Er schreibt Dateien des Nutzers, deshalb geht es
/// hier fast durchgehend darum, dass er in Zweifelsfällen <em>nichts</em> tut: kein Schreiben
/// ohne Bearbeiten-Modus, keine Tag-Änderung an einer gesperrten Datei, und eine verständliche
/// Meldung statt eines Absturzes, wenn das Schreiben scheitert.
/// </summary>
public sealed class MarkdownEditorViewModelEdgeCaseTests
{
    private const string Testpfad = @"C:\notizen\datei.md";

    [Fact]
    public void EnterEditMode_WithoutAFile_StaysInReadMode()
    {
        using MarkdownEditorViewModel sut = Erzeuge(new FakeFileSystem());

        sut.EnterEditMode();

        Assert.True(sut.IsLocked);
        Assert.Null(sut.StatusMessage);
    }

    [Fact]
    public async Task ExitEditMode_ReturnsToReadMode()
    {
        FakeFileSystem fs = ErzeugeDateisystem("alt");
        using MarkdownEditorViewModel sut = Erzeuge(fs);
        await sut.LoadAsync(Guid.NewGuid(), Testpfad, CancellationToken.None).ConfigureAwait(true);
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
        using MarkdownEditorViewModel sut = Erzeuge(fs);

        await sut.SaveAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(fs.WrittenFiles);
    }

    [Fact]
    public async Task SaveAsync_WhenWritingFails_ReportsItAndKeepsTheChanges()
    {
        // Der Text darf auf keinen Fall als gespeichert gelten, sonst geht die Arbeit
        // beim nächsten Wechsel der Datei verloren.
        FakeFileSystem fs = ErzeugeDateisystem("alt");
        fs.FailOnWrite = new IOException("Datei ist belegt");
        using MarkdownEditorViewModel sut = Erzeuge(fs);
        await sut.LoadAsync(Guid.NewGuid(), Testpfad, CancellationToken.None).ConfigureAwait(true);
        sut.EnterEditMode();
        sut.Text = "neu";

        await sut.SaveAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Contains("fehlgeschlagen", sut.StatusMessage!, StringComparison.Ordinal);
        Assert.True(sut.IsDirty);
        Assert.False(sut.IsSaving);
    }

    [Fact]
    public async Task SaveAsync_WhenAccessIsDenied_ReportsItAndKeepsTheChanges()
    {
        FakeFileSystem fs = ErzeugeDateisystem("alt");
        fs.FailOnWrite = new UnauthorizedAccessException("Kein Zugriff");
        using MarkdownEditorViewModel sut = Erzeuge(fs);
        await sut.LoadAsync(Guid.NewGuid(), Testpfad, CancellationToken.None).ConfigureAwait(true);
        sut.EnterEditMode();
        sut.Text = "neu";

        await sut.SaveAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Contains("fehlgeschlagen", sut.StatusMessage!, StringComparison.Ordinal);
        Assert.True(sut.IsDirty);
    }

    [Fact]
    public async Task SaveAsync_WhenTheFileNoLongerExists_WritesItAnyway()
    {
        // Nach einem Direktladen liegt die Datei nicht im Bestand. Die Prüfung auf
        // Fremdänderung muss das durchlassen, sonst lässt sich eine neue Datei nie speichern.
        FakeFileSystem fs = new();
        using MarkdownEditorViewModel sut = Erzeuge(fs);
        await sut.LoadDirectAsync(Testpfad, "Inhalt", CancellationToken.None).ConfigureAwait(true);
        sut.EnterEditMode();
        sut.Text = "geänderter Text";

        await sut.SaveAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.True(fs.WrittenFiles.ContainsKey(Testpfad));
        Assert.False(sut.IsDirty);
    }

    [Fact]
    public async Task AddTag_WhileLocked_ChangesNothing()
    {
        FakeFileSystem fs = ErzeugeDateisystem("Text");
        using MarkdownEditorViewModel sut = Erzeuge(fs);
        await sut.LoadAsync(Guid.NewGuid(), Testpfad, CancellationToken.None).ConfigureAwait(true);
        sut.TagInput = "bericht";

        sut.AddTag();

        Assert.Equal("Text", sut.Text, StringComparer.Ordinal);
    }

    [Fact]
    public async Task AddTag_WithOnlyAHashSign_ChangesNothing()
    {
        FakeFileSystem fs = ErzeugeDateisystem("Text");
        using MarkdownEditorViewModel sut = Erzeuge(fs);
        await sut.LoadAsync(Guid.NewGuid(), Testpfad, CancellationToken.None).ConfigureAwait(true);
        sut.EnterEditMode();
        sut.TagInput = "#";

        sut.AddTag();

        Assert.Equal("Text", sut.Text, StringComparer.Ordinal);
    }

    [Fact]
    public async Task RemoveTag_WhileLocked_ChangesNothing()
    {
        FakeFileSystem fs = ErzeugeDateisystem("Text mit #bericht.");
        using MarkdownEditorViewModel sut = Erzeuge(fs);
        await sut.LoadAsync(Guid.NewGuid(), Testpfad, CancellationToken.None).ConfigureAwait(true);

        sut.RemoveTag("bericht");

        Assert.Contains("#bericht", sut.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveTag_WithoutAName_ChangesNothing()
    {
        FakeFileSystem fs = ErzeugeDateisystem("Text mit #bericht.");
        using MarkdownEditorViewModel sut = Erzeuge(fs);
        await sut.LoadAsync(Guid.NewGuid(), Testpfad, CancellationToken.None).ConfigureAwait(true);
        sut.EnterEditMode();

        sut.RemoveTag("   ");

        Assert.Contains("#bericht", sut.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameTag_WhileLocked_ChangesNothing()
    {
        FakeFileSystem fs = ErzeugeDateisystem("Text mit #bericht.");
        using MarkdownEditorViewModel sut = Erzeuge(fs);
        await sut.LoadAsync(Guid.NewGuid(), Testpfad, CancellationToken.None).ConfigureAwait(true);

        sut.RenameTag("bericht", "notiz");

        Assert.Contains("#bericht", sut.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenameTag_WithoutNames_Throws()
    {
        using MarkdownEditorViewModel sut = Erzeuge(new FakeFileSystem());

        _ = Assert.Throws<ArgumentException>(() => sut.RenameTag("   ", "notiz"));
        _ = Assert.Throws<ArgumentException>(() => sut.RenameTag("bericht", "   "));
    }

    [Fact]
    public async Task LoadDirectAsync_WithoutText_Throws()
    {
        using MarkdownEditorViewModel sut = Erzeuge(new FakeFileSystem());

        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.LoadDirectAsync(Testpfad, null!, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        MarkdownEditorViewModel sut = Erzeuge(new FakeFileSystem());

        sut.Dispose();
        sut.Dispose();
    }

    [Fact]
    public async Task HasUnsavedChanges_MirrorsTheDirtyFlag()
    {
        // Das Hauptfenster fragt diese Eigenschaft beim Schließen ab.
        FakeFileSystem fs = ErzeugeDateisystem("alt");
        using MarkdownEditorViewModel sut = Erzeuge(fs);
        await sut.LoadAsync(Guid.NewGuid(), Testpfad, CancellationToken.None).ConfigureAwait(true);
        Assert.False(sut.HasUnsavedChanges);

        sut.EnterEditMode();
        sut.Text = "neu";

        Assert.True(sut.HasUnsavedChanges);
    }

    [Fact]
    public async Task Constructor_WithConfirmationDialog_UsesTheDefaultDebounce()
    {
        // Der Weg, den die Anwendung selbst nimmt — mit Bestätigungsdialog und ohne
        // ausdrücklich gesetzte Verzögerung.
        FakeFileSystem fs = ErzeugeDateisystem("Text mit #bericht.");
        using MarkdownEditorViewModel sut = new(
            fs,
            new TagExtractor(new StubSettings()),
            TimeProvider.System,
            new AlwaysConfirms(),
            NullLogger<MarkdownEditorViewModel>.Instance);

        await sut.LoadAsync(Guid.NewGuid(), Testpfad, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("Text mit #bericht.", sut.Text, StringComparer.Ordinal);
        Assert.Contains("bericht", sut.Tags, StringComparer.OrdinalIgnoreCase);
    }

    private static FakeFileSystem ErzeugeDateisystem(string inhalt)
    {
        FakeFileSystem fs = new();
        fs.Files[Testpfad] = Encoding.UTF8.GetBytes(inhalt);
        return fs;
    }

    private static MarkdownEditorViewModel Erzeuge(FakeFileSystem fs) =>
        new(fs,
            new TagExtractor(new StubSettings()),
            TimeProvider.System,
            NullLogger<MarkdownEditorViewModel>.Instance,
            TimeSpan.Zero);

    private sealed class AlwaysConfirms : IEditorConfirmationDialogService
    {
        public bool ConfirmSave() => true;
    }

    private sealed class StubSettings : ISettingsService
    {
        public AppSettings Current { get; private set; } = AppSettings.Default;

        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(settings);
            AppSettings vorher = Current;
            Current = settings;
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(vorher, settings));
            return Task.CompletedTask;
        }
    }
}
