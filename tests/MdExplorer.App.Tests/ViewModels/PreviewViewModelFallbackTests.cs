using System.IO;
using System.Text;
using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Prüft die Ausweichpfade der Vorschau. Sie sind der Unterschied zwischen „Vorschau bleibt
/// leer" und „Programm stürzt beim Anklicken einer Datei ab": Ein Lookup kann abgebrochen
/// werden, die Datenbank kann kurzzeitig belegt sein, und ein gerade erst angelegtes
/// Dokument hat noch gar kein gerendertes HTML.
/// </summary>
public sealed class PreviewViewModelFallbackTests
{
    private static readonly DateTime FixedTime = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task LoadAsync_WithoutAFile_ShowsTheEmptyDocumentAndClearsTheCurrentId()
    {
        // Guid.Empty steht für "nichts ausgewählt", etwa nach dem Abwählen im Baum.
        FakeMarkdownDocumentRepository repo = new();
        using ServiceProvider provider = CreateProvider(repo);
        PreviewViewModel sut = Create(provider);
        await sut.LoadAsync(new Guid("eeeeeeee-0000-0000-0000-000000000001"), TestContext.Current.CancellationToken).ConfigureAwait(true);

        await sut.LoadAsync(Guid.Empty, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Null(sut.CurrentDocumentId);
        // Auch ohne Inhalt trägt die Anzeige die Belegung. Ein blankes HTML-Gerüst stünde im
        // dunklen Erscheinungsbild als weiße Fläche über der halben Anwendung.
        Assert.Contains("Content-Security-Policy", sut.Html, StringComparison.Ordinal);
        Assert.Contains("<body></body>", sut.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_WhenTheLookupIsCancelled_LeavesTheShownDocumentUntouched()
    {
        // Beim schnellen Durchklicken bricht der vorige Ladevorgang ab. Würde er dabei die
        // Anzeige leeren, flösse die gerade fertig geladene Vorschau wieder weg.
        Guid fileId = new("eeeeeeee-0000-0000-0000-000000000002");
        FakeMarkdownDocumentRepository repo = new();
        repo.Put(fileId, CreateDocument(fileId, "<h1>Bestand</h1>"));
        using ServiceProvider provider = CreateProvider(repo);
        PreviewViewModel sut = Create(provider);
        await sut.LoadAsync(fileId, TestContext.Current.CancellationToken).ConfigureAwait(true);
        string before = sut.Html;

        repo.FailOnGet = new OperationCanceledException();
        await sut.LoadAsync(new Guid("eeeeeeee-0000-0000-0000-000000000003"), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(before, sut.Html, StringComparer.Ordinal);
        Assert.Equal(fileId, sut.CurrentDocumentId);
    }

    [Theory]
    [InlineData(TestFailures.Database)]
    [InlineData(TestFailures.InvalidState)]
    public async Task LoadAsync_WhenTheLookupFails_ShowsTheEmptyDocumentForThatFile(string kind)
    {
        Guid fileId = new("eeeeeeee-0000-0000-0000-000000000004");
        FakeMarkdownDocumentRepository repo = new() { FailOnGet = TestFailures.Of(kind) };
        using ServiceProvider provider = CreateProvider(repo);
        PreviewViewModel sut = Create(provider);

        await sut.LoadAsync(fileId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        // Die Datei gilt als angezeigt, sonst lädt die Oberfläche endlos dieselbe Datei nach.
        Assert.Equal(fileId, sut.CurrentDocumentId);
        Assert.Contains("Content-Security-Policy", sut.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_WithoutRenderedHtml_ShowsAnEmptyBody()
    {
        // Kommt vor, solange der Parser die frisch angelegte Datei noch nicht verarbeitet hat.
        Guid fileId = new("eeeeeeee-0000-0000-0000-000000000005");
        FakeMarkdownDocumentRepository repo = new();
        MarkdownDocument document = new()
        {
            Id = new Guid("eeeeeeee-0000-0000-0000-000000000006"),
            MarkdownFileId = fileId,
            SourceContentHash = "hash",
            FrontmatterJson = "{}",
            OutlinksJson = "[]",
            ParsedAtUtc = FixedTime,
        };
        repo.Put(fileId, document);
        using ServiceProvider provider = CreateProvider(repo);
        PreviewViewModel sut = Create(provider);

        await sut.LoadAsync(fileId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(fileId, sut.CurrentDocumentId);
        Assert.Contains("Content-Security-Policy", sut.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowUnindexedFile_WithoutAPath_Throws()
    {
        FakeMarkdownDocumentRepository repo = new();
        using ServiceProvider provider = CreateProvider(repo);
        PreviewViewModel sut = Create(provider);

        _ = Assert.Throws<ArgumentException>(() => sut.ShowUnindexedFile(ReadOnlyMemory<byte>.Empty, " "));
    }

    [Fact]
    public void ShowRendered_WithEmptyContent_ShowsAnEmptyDocument()
    {
        FakeMarkdownDocumentRepository repo = new();
        using ServiceProvider provider = CreateProvider(repo);
        PreviewViewModel sut = Create(provider);

        sut.ShowRendered(ReadOnlyMemory<byte>.Empty);

        Assert.Contains("Content-Security-Policy", sut.Html, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Kaputtes HTML mit Zehntausenden offener <c>&lt;img</c> ohne Ende lässt das Muster für die
    /// Bildpfade quadratisch suchen, bis seine Zeitgrenze von einer Sekunde reißt. Das Dokument
    /// erscheint dann ohne umgeschriebene Bilder; die Ausnahme darf den Klick nicht abbrechen.
    /// </remarks>
    [Fact]
    public void ShowUnindexedFile_WhenRewritingTheImagesTimesOut_ShowsTheDocumentAnyway()
    {
        string broken = "<p>Anfang</p>" + string.Concat(Enumerable.Repeat("<img alt=x ", 60_000));
        using ServiceProvider provider = CreateProvider(new FakeMarkdownDocumentRepository());
        PreviewViewModel sut = Create(provider);

        sut.ShowUnindexedFile(TestGzip.Compress(broken), @"C:\notes\kaputt.md");

        Assert.Contains("<p>Anfang</p>", sut.Html, StringComparison.Ordinal);
        Assert.Contains("</body></html>", sut.Html, StringComparison.Ordinal);
    }

    private static PreviewViewModel Create(ServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(),
            new PreviewHtmlBuilder(new FakeThemeProvider(isDarkMode: false), new FakeSettingsService(AppSettings.Default)),
            NullLogger<PreviewViewModel>.Instance);

    private static ServiceProvider CreateProvider(FakeMarkdownDocumentRepository repository)
    {
        ServiceCollection services = new();
        _ = services.AddScoped<IMarkdownDocumentRepository>(_ => repository);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static MarkdownDocument CreateDocument(Guid fileId, string html)
    {
        MarkdownDocument document = new()
        {
            Id = new Guid("eeeeeeee-0000-0000-0000-000000000007"),
            MarkdownFileId = fileId,
            SourceContentHash = "hash",
            FrontmatterJson = "{}",
            OutlinksJson = "[]",
            ParsedAtUtc = FixedTime,
        };
        document.SetRenderedHtmlGz(TestGzip.Compress(html));
        return document;
    }
}
