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

/// <summary>Tests für die Preview-HTML-Pipeline und die CSP-Verpackung.</summary>
public sealed class PreviewViewModelTests
{
    private static readonly DateTime FixedUtc = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task LoadAsync_BuildsHtmlWithCspAndBody()
    {
        Guid fileId = Guid.NewGuid();
        FakeMarkdownDocumentRepository repo = new();
        MarkdownDocument document = new()
        {
            Id = Guid.NewGuid(),
            MarkdownFileId = fileId,
            SourceContentHash = "hash",
            FrontmatterJson = "{}",
            OutlinksJson = "[]",
            ParsedAtUtc = FixedUtc,
        };
        document.SetRenderedHtmlGz(Gzip("<h1>Titel</h1>"));
        repo.Put(fileId, document);

        PreviewHtmlBuilder builder = new(new FakeThemeProvider(isDarkMode: false));
        using ServiceProvider provider = BuildProvider(repo);
        PreviewViewModel vm = new(provider.GetRequiredService<IServiceScopeFactory>(), builder, NullLogger<PreviewViewModel>.Instance);

        await vm.LoadAsync(fileId, CancellationToken.None).ConfigureAwait(true);

        Assert.Contains("<h1>Titel</h1>", vm.Html, StringComparison.Ordinal);
        Assert.Contains("Content-Security-Policy", vm.Html, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", vm.Html, StringComparison.Ordinal);
        Assert.Equal(fileId, vm.CurrentDocumentId);
    }

    [Fact]
    public async Task LoadAsync_MissingDocument_StillReturnsEmptyHtmlWithCsp()
    {
        FakeMarkdownDocumentRepository repo = new();
        PreviewHtmlBuilder builder = new(new FakeThemeProvider(isDarkMode: true));
        using ServiceProvider provider = BuildProvider(repo);
        PreviewViewModel vm = new(provider.GetRequiredService<IServiceScopeFactory>(), builder, NullLogger<PreviewViewModel>.Instance);

        await vm.LoadAsync(Guid.NewGuid(), CancellationToken.None).ConfigureAwait(true);

        Assert.Contains("Content-Security-Policy", vm.Html, StringComparison.Ordinal);
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
        PreviewHtmlBuilder builder = new(new FakeThemeProvider(isDarkMode: false));

        string html = builder.Build("<script>alert(1)</script>");

        // Auch wenn der Markdown-Pfad HTML einschleust, der CSP-Header laesst keinen Script-Lauf zu.
        Assert.Contains("script-src 'none'", html, StringComparison.Ordinal);
    }

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

    private static ServiceProvider BuildProvider(FakeMarkdownDocumentRepository repository)
    {
        ServiceCollection services = new();
        _ = services.AddScoped<IMarkdownDocumentRepository>(_ => repository);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static byte[] Gzip(string text)
    {
        using MemoryStream output = new();
        using (GZipStream gz = new(output, CompressionLevel.Fastest))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            gz.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }
}
