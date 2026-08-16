using System.Data.Common;
using System.IO;
using System.IO.Compression;
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
    private static readonly DateTime FesteZeit = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task LoadAsync_WithoutAFile_ShowsTheEmptyDocumentAndClearsTheCurrentId()
    {
        // Guid.Empty steht für "nichts ausgewählt" — etwa nach dem Abwählen im Baum.
        FakeMarkdownDocumentRepository repo = new();
        using ServiceProvider provider = ErzeugeAnbieter(repo);
        PreviewViewModel sut = Erzeuge(provider);
        await sut.LoadAsync(Guid.NewGuid(), TestContext.Current.CancellationToken).ConfigureAwait(true);

        await sut.LoadAsync(Guid.Empty, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Null(sut.CurrentDocumentId);
        // Auch ohne Inhalt trägt die Anzeige die Belegung: Ein blankes HTML-Gerüst stand im
        // dunklen Erscheinungsbild als weiße Fläche über der halben Anwendung.
        Assert.Contains("Content-Security-Policy", sut.Html, StringComparison.Ordinal);
        Assert.Contains("<body></body>", sut.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_WhenTheLookupIsCancelled_LeavesTheShownDocumentUntouched()
    {
        // Beim schnellen Durchklicken bricht der vorige Ladevorgang ab. Würde er dabei die
        // Anzeige leeren, floeße die gerade fertig geladene Vorschau wieder weg.
        Guid fileId = Guid.NewGuid();
        FakeMarkdownDocumentRepository repo = new();
        repo.Put(fileId, ErzeugeDokument(fileId, "<h1>Bestand</h1>"));
        using ServiceProvider provider = ErzeugeAnbieter(repo);
        PreviewViewModel sut = Erzeuge(provider);
        await sut.LoadAsync(fileId, TestContext.Current.CancellationToken).ConfigureAwait(true);
        string vorher = sut.Html;

        repo.FailOnGet = new OperationCanceledException();
        await sut.LoadAsync(Guid.NewGuid(), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(vorher, sut.Html, StringComparer.Ordinal);
        Assert.Equal(fileId, sut.CurrentDocumentId);
    }

    [Fact]
    public async Task LoadAsync_OnDbException_ShowsTheEmptyDocumentForThatFile()
    {
        Guid fileId = Guid.NewGuid();
        FakeMarkdownDocumentRepository repo = new() { FailOnGet = new TestDbException("Datenbank belegt") };
        using ServiceProvider provider = ErzeugeAnbieter(repo);
        PreviewViewModel sut = Erzeuge(provider);

        await sut.LoadAsync(fileId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        // Die Datei gilt als angezeigt — sonst lädt die Oberfläche endlos dieselbe Datei nach.
        Assert.Equal(fileId, sut.CurrentDocumentId);
        Assert.Contains("Content-Security-Policy", sut.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_WithoutRenderedHtml_ShowsAnEmptyBody()
    {
        // Kommt vor, solange der Parser die frisch angelegte Datei noch nicht verarbeitet hat.
        Guid fileId = Guid.NewGuid();
        FakeMarkdownDocumentRepository repo = new();
        MarkdownDocument dokument = new()
        {
            Id = Guid.NewGuid(),
            MarkdownFileId = fileId,
            SourceContentHash = "hash",
            FrontmatterJson = "{}",
            OutlinksJson = "[]",
            ParsedAtUtc = FesteZeit,
        };
        repo.Put(fileId, dokument);
        using ServiceProvider provider = ErzeugeAnbieter(repo);
        PreviewViewModel sut = Erzeuge(provider);

        await sut.LoadAsync(fileId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(fileId, sut.CurrentDocumentId);
        Assert.Contains("Content-Security-Policy", sut.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void SetHtml_WithoutHtml_Throws()
    {
        FakeMarkdownDocumentRepository repo = new();
        using ServiceProvider provider = ErzeugeAnbieter(repo);
        PreviewViewModel sut = Erzeuge(provider);

        _ = Assert.Throws<ArgumentNullException>(() => sut.SetHtml(null!));
    }

    [Fact]
    public void SetHtml_WithHtml_ReplacesTheShownDocument()
    {
        FakeMarkdownDocumentRepository repo = new();
        using ServiceProvider provider = ErzeugeAnbieter(repo);
        PreviewViewModel sut = Erzeuge(provider);

        sut.SetHtml("<html>direkt</html>");

        Assert.Equal("<html>direkt</html>", sut.Html, StringComparer.Ordinal);
    }

    private static PreviewViewModel Erzeuge(ServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(),
            new PreviewHtmlBuilder(new FakeThemeProvider(isDarkMode: false)),
            NullLogger<PreviewViewModel>.Instance);

    private static ServiceProvider ErzeugeAnbieter(FakeMarkdownDocumentRepository repository)
    {
        ServiceCollection dienste = new();
        _ = dienste.AddScoped<IMarkdownDocumentRepository>(_ => repository);
        return dienste.BuildServiceProvider(validateScopes: true);
    }

    private static MarkdownDocument ErzeugeDokument(Guid fileId, string html)
    {
        MarkdownDocument dokument = new()
        {
            Id = Guid.NewGuid(),
            MarkdownFileId = fileId,
            SourceContentHash = "hash",
            FrontmatterJson = "{}",
            OutlinksJson = "[]",
            ParsedAtUtc = FesteZeit,
        };
        dokument.SetRenderedHtmlGz(Packe(html));
        return dokument;
    }

    private static byte[] Packe(string text)
    {
        using MemoryStream ausgabe = new();
        using (GZipStream gz = new(ausgabe, CompressionLevel.Fastest))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            gz.Write(bytes, 0, bytes.Length);
        }
        return ausgabe.ToArray();
    }

    /// <summary><see cref="DbException"/> ist abstrakt — für den Fehlerpfad braucht es eine eigene Ausprägung.</summary>
    private sealed class TestDbException : DbException
    {
        public TestDbException()
        {
        }

        public TestDbException(string message)
            : base(message)
        {
        }

        public TestDbException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
