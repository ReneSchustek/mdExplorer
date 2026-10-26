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
    private const string SecondPath = @"C:\Notizen\Zwei.md";
    private const string ThirdPath = @"C:\Notizen\Drei.md";

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
    public async Task OpenAsync_WhileAnotherOpens_RunsTheYoungestAfterwards()
    {
        // Die Rückfrage zu ungespeicherten Änderungen hält das erste Öffnen offen. Zwei weitere
        // Doppelklicks in dieser Zeit dürfen keine zweite Rückfrage darüberlegen; danach gilt der
        // jüngste.
        TaskCompletionSource<bool> firstOpen = new();
        List<string> opened = [];
        ExternalFileOpener sut = new(
            path =>
            {
                opened.Add(path);
                return opened.Count == 1 ? firstOpen.Task : Task.FromResult(true);
            },
            _dialogs,
            NullLogger<ExternalFileOpener>.Instance);

        Task first = sut.OpenAsync(MarkdownPathCheck.Accepted(NotePath));
        await sut.OpenAsync(MarkdownPathCheck.Accepted(SecondPath)).ConfigureAwait(true);
        await sut.OpenAsync(MarkdownPathCheck.Accepted(ThirdPath)).ConfigureAwait(true);
        Assert.Equal([NotePath], opened);

        firstOpen.SetResult(true);
        await first.ConfigureAwait(true);

        Assert.Equal([NotePath, ThirdPath], opened);
    }

    [Fact]
    public async Task OpenAsync_AfterTheQueueIsDone_OpensRightAway()
    {
        ExternalFileOpener sut = CreateSut();

        await sut.OpenAsync(MarkdownPathCheck.Accepted(NotePath)).ConfigureAwait(true);
        await sut.OpenAsync(MarkdownPathCheck.Accepted(SecondPath)).ConfigureAwait(true);

        Assert.Equal([NotePath, SecondPath], _opened);
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
