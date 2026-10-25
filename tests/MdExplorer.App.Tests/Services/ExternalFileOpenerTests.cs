using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.Services;

/// <summary>
/// Eine Datei von außen geht in den Dokumentbereich; ein abgewiesener Pfad wird dem Nutzer
/// gemeldet, statt still zu verschwinden.
/// </summary>
public sealed class ExternalFileOpenerTests
{
    private const string NotePath = @"C:\Notizen\Plan.md";

    private readonly FakeDialogService _dialogs = new();
    private readonly List<string> _opened = [];
    private bool _openResult = true;

    [Fact]
    public async Task OpenAsync_WithAnAcceptedPath_OpensTheFile()
    {
        await CreateSut().OpenAsync(MarkdownPathCheck.Accepted(NotePath)).ConfigureAwait(true);

        Assert.Equal([NotePath], _opened);
        Assert.Null(_dialogs.LastError);
    }

    [Fact]
    public async Task OpenAsync_WhenTheUserKeepsTheUnsavedDocument_ShowsNoError()
    {
        // Abbrechen bei der Rückfrage ist eine Antwort des Nutzers, kein Fehler.
        _openResult = false;

        await CreateSut().OpenAsync(MarkdownPathCheck.Accepted(NotePath)).ConfigureAwait(true);

        Assert.Equal([NotePath], _opened);
        Assert.Null(_dialogs.LastError);
    }

    [Theory]
    [InlineData(nameof(MarkdownPathProblem.NotFound), "wurde nicht gefunden")]
    [InlineData(nameof(MarkdownPathProblem.UnsupportedExtension), ".md oder .markdown")]
    [InlineData(nameof(MarkdownPathProblem.InvalidPath), "kein gültiger Dateipfad")]
    public async Task OpenAsync_WithARejectedPath_ReportsWhyAndOpensNothing(string problem, string expected)
    {
        MarkdownPathCheck rejected = MarkdownPathCheck.Rejected(@"C:\Notizen\Plan.txt", Enum.Parse<MarkdownPathProblem>(problem));

        await CreateSut().OpenAsync(rejected).ConfigureAwait(true);

        Assert.Empty(_opened);
        (string title, string message) = Assert.NotNull(_dialogs.LastError);
        Assert.Equal("Datei kann nicht geöffnet werden", title, StringComparer.Ordinal);
        Assert.Contains(expected, message, StringComparison.Ordinal);
        Assert.Contains(@"C:\Notizen\Plan.txt", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAsync_WithoutAPath_DoesNothing()
    {
        await CreateSut().OpenAsync(MarkdownPathCheck.None).ConfigureAwait(true);

        Assert.Empty(_opened);
        Assert.Null(_dialogs.LastError);
    }

    [Fact]
    public void Rejected_WithAnAcceptedCheck_Throws()
    {
        _ = Assert.Throws<ArgumentException>(() => OpenFileMessages.Rejected(MarkdownPathCheck.Accepted(NotePath)));
    }

    [Fact]
    public void HandOverFailed_NamesTheFileWhenThereIsOne()
    {
        Assert.Contains(NotePath, OpenFileMessages.HandOverFailed(NotePath), StringComparison.Ordinal);
        Assert.DoesNotContain("Datei", OpenFileMessages.HandOverFailed(absolutePath: null), StringComparison.Ordinal);
        Assert.Equal("MdExplorer antwortet nicht", OpenFileMessages.HandOverFailedTitle, StringComparer.Ordinal);
    }

    private ExternalFileOpener CreateSut() => new(
        path =>
        {
            _opened.Add(path);
            return Task.FromResult(_openResult);
        },
        _dialogs,
        NullLogger<ExternalFileOpener>.Instance);
}
