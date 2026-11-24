using System.IO;
using System.Net;
using System.Text;
using MdExplorer.App.Localization;
using MdExplorer.App.Services;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Die Vorschau entpackt das gespeicherte HTML und liefert es nur mit der Richtlinie aus, die
/// jedes Skript sperrt, auch wenn das Dokument fehlt oder selbst ein Skript mitbringt.
/// </summary>
public sealed class PreviewViewModelTests
{
    private static readonly DateTime FixedUtc = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task LoadAsync_BuildsHtmlWithCspAndBody()
    {
        Guid fileId = new("eeeeeeee-0000-0000-0000-000000000008");
        FakeMarkdownDocumentRepository repo = new();
        MarkdownDocument document = new()
        {
            Id = new Guid("eeeeeeee-0000-0000-0000-000000000009"),
            MarkdownFileId = fileId,
            SourceContentHash = "hash",
            FrontmatterJson = "{}",
            OutlinksJson = "[]",
            ParsedAtUtc = FixedUtc,
        };
        document.SetRenderedHtmlGz(TestGzip.Compress("<h1>Titel</h1>"));
        repo.Put(fileId, document);

        PreviewHtmlBuilder builder = new(new FakeThemeProvider(isDarkMode: false), new FakeSettingsService(AppSettings.Default));
        using ServiceProvider provider = BuildProvider(repo);
        PreviewViewModel vm = new(provider.GetRequiredService<IServiceScopeFactory>(), builder, NullLogger<PreviewViewModel>.Instance);

        await vm.LoadAsync(fileId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Contains("<h1>Titel</h1>", vm.Html, StringComparison.Ordinal);
        Assert.Contains("Content-Security-Policy", vm.Html, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", vm.Html, StringComparison.Ordinal);
        Assert.Equal(fileId, vm.CurrentDocumentId);
    }

    [Fact]
    public async Task LoadAsync_MissingDocument_StillReturnsEmptyHtmlWithCsp()
    {
        FakeMarkdownDocumentRepository repo = new();
        PreviewHtmlBuilder builder = new(new FakeThemeProvider(isDarkMode: true), new FakeSettingsService(AppSettings.Default));
        using ServiceProvider provider = BuildProvider(repo);
        PreviewViewModel vm = new(provider.GetRequiredService<IServiceScopeFactory>(), builder, NullLogger<PreviewViewModel>.Instance);

        await vm.LoadAsync(new Guid("eeeeeeee-0000-0000-0000-000000000010"), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Contains("Content-Security-Policy", vm.Html, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Eine leere Fläche sähe aus wie eine leere Datei. Wer eine Notiz öffnet und nichts sieht,
    /// soll erfahren, dass die Vorschau gerade nicht geladen werden konnte.
    /// </remarks>
    [Fact]
    public async Task LoadAsync_WhenTheIndexFails_SaysSoInsteadOfShowingAnEmptyPage()
    {
        Guid fileId = new("eeeeeeee-0000-0000-0000-000000000011");
        FakeMarkdownDocumentRepository repo = new() { FailOnGet = new TestDbException("database is locked") };
        using ServiceProvider provider = BuildProvider(repo);
        PreviewViewModel vm = NewViewModel(provider);

        await vm.LoadAsync(fileId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Contains(WebUtility.HtmlEncode(UiText.Get("Preview_LoadFailed")), vm.Html, StringComparison.Ordinal);
        Assert.Equal(fileId, vm.CurrentDocumentId);
    }

    [Fact]
    public async Task LoadAsync_WhenTheStoredHtmlIsDamaged_SaysSoToo()
    {
        Guid fileId = new("eeeeeeee-0000-0000-0000-000000000012");
        FakeMarkdownDocumentRepository repo = new();
        // Vollständiger gzip-Kopf, danach ein Deflate-Block vom unzulässigen Typ 11.
        repo.Put(fileId, DocumentWith(fileId, [0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0B, 0xFF, 0xFF, 0xFF, 0xFF]));
        using ServiceProvider provider = BuildProvider(repo);
        PreviewViewModel vm = NewViewModel(provider);

        await vm.LoadAsync(fileId, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Contains(WebUtility.HtmlEncode(UiText.Get("Preview_LoadFailed")), vm.Html, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Ein Klick, der schon überholt ist, darf mit seinem Fehler nicht die Seite des neueren
    /// überschreiben: Der Nutzer sähe die richtige Datei markiert und eine fremde Meldung.
    /// </remarks>
    [Fact]
    public async Task LoadAsync_WhenASupersededLoadFails_KeepsTheCurrentPage()
    {
        Guid shown = new("eeeeeeee-0000-0000-0000-000000000013");
        FakeMarkdownDocumentRepository repo = new();
        repo.Put(shown, DocumentWith(shown, TestGzip.Compress("<h1>Aktuell</h1>")));
        using ServiceProvider provider = BuildProvider(repo);
        PreviewViewModel vm = NewViewModel(provider);
        await vm.LoadAsync(shown, TestContext.Current.CancellationToken).ConfigureAwait(true);
        repo.FailOnGet = new TestDbException("database is locked");
        using CancellationTokenSource superseded = new();
        await superseded.CancelAsync().ConfigureAwait(true);

        await vm.LoadAsync(new Guid("eeeeeeee-0000-0000-0000-000000000014"), superseded.Token).ConfigureAwait(true);

        Assert.Contains("<h1>Aktuell</h1>", vm.Html, StringComparison.Ordinal);
        Assert.Equal(shown, vm.CurrentDocumentId);
    }

    [Fact]
    public void ContentSecurityPolicy_DisallowsAnyScriptSource()
    {
        Assert.Contains("script-src 'none'", PreviewHtmlBuilder.ContentSecurityPolicy, StringComparison.Ordinal);
        Assert.DoesNotContain("script-src 'self'", PreviewHtmlBuilder.ContentSecurityPolicy, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-inline'", new ScriptSrcSlice(PreviewHtmlBuilder.ContentSecurityPolicy).Value, StringComparison.Ordinal);
    }

    [Fact]
    public void ContentSecurityPolicy_DropsFileSchemeFromImageSources()
    {
        Assert.DoesNotContain(" file:", PreviewHtmlBuilder.ContentSecurityPolicy, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OnScriptPayload_KeepsScriptSrcNoneIntact()
    {
        PreviewHtmlBuilder builder = new(new FakeThemeProvider(isDarkMode: false), new FakeSettingsService(AppSettings.Default));

        string html = builder.Build("<script>alert(1)</script>");

        // Auch wenn der Markdown-Pfad HTML einschleust, der CSP-Header lässt keinen Script-Lauf zu.
        Assert.Contains("script-src 'none'", html, StringComparison.Ordinal);
    }

    /// <summary>Die <c>script-src</c>-Anweisung der Richtlinie bis zum nächsten Semikolon.</summary>
    private readonly record struct ScriptSrcSlice(string Policy)
    {
        public string Value
        {
            get
            {
                const string Marker = "script-src";
                int start = Policy.IndexOf(Marker, StringComparison.Ordinal);
                if (start < 0)
                {
                    return string.Empty;
                }
                int end = Policy.IndexOf(';', start);
                return end < 0 ? Policy[start..] : Policy[start..end];
            }
        }
    }

    private static PreviewViewModel NewViewModel(ServiceProvider provider) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new PreviewHtmlBuilder(new FakeThemeProvider(isDarkMode: false), new FakeSettingsService(AppSettings.Default)),
            NullLogger<PreviewViewModel>.Instance);

    private static MarkdownDocument DocumentWith(Guid fileId, byte[] renderedHtmlGz)
    {
        MarkdownDocument document = new()
        {
            Id = new Guid("eeeeeeee-0000-0000-0000-0000000000ff"),
            MarkdownFileId = fileId,
            SourceContentHash = "hash",
            FrontmatterJson = "{}",
            OutlinksJson = "[]",
            ParsedAtUtc = FixedUtc,
        };
        document.SetRenderedHtmlGz(renderedHtmlGz);
        return document;
    }

    private static ServiceProvider BuildProvider(FakeMarkdownDocumentRepository repository)
    {
        ServiceCollection services = new();
        _ = services.AddScoped<IMarkdownDocumentRepository>(_ => repository);
        return services.BuildServiceProvider(validateScopes: true);
    }
}
