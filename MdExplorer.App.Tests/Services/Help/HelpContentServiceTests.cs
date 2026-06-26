using MdExplorer.App.Services.Help;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.Services.Help;

/// <summary>
/// Integrationstests gegen das echte eingebettete Handbuch. Der Service liest
/// die Ressource aus der App-Assembly — wir wollen sicherstellen, dass die
/// stabilen Slugs aus <see cref="HelpContext"/> tatsächlich im Handbuch
/// vorkommen, der HTML-Output Anker enthält und der Plaintext nicht leer ist.
/// </summary>
public sealed class HelpContentServiceTests
{
    [Fact]
    public async Task GetAsync_LoadsEmbeddedManualWithTocAndHtml()
    {
        using HelpContentService service = new(NullLogger<HelpContentService>.Instance);

        HelpContent content = await service.GetAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.False(string.IsNullOrWhiteSpace(content.Html), "HTML darf nicht leer sein.");
        Assert.True(content.PlainText.Length > 1000, "Plaintext muss substanziell sein.");
        Assert.NotEmpty(content.Toc);
        Assert.Contains(content.Toc, entry => entry.Slug == HelpContext.Install);
        Assert.Contains(content.Toc, entry => entry.Slug == HelpContext.Search);
        Assert.Contains(content.Toc, entry => entry.Slug == HelpContext.Graph);
    }

    [Fact]
    public async Task GetAsync_TocSlugsAppearInHtmlAsAnchorIds()
    {
        using HelpContentService service = new(NullLogger<HelpContentService>.Instance);

        HelpContent content = await service.GetAsync(CancellationToken.None).ConfigureAwait(true);

        foreach (HelpTocEntry entry in content.Toc)
        {
            string expectedAnchor = $"id=\"{entry.Slug}\"";
            Assert.Contains(expectedAnchor, content.Html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task GetAsync_CalledTwice_ReturnsSameCachedInstance()
    {
        using HelpContentService service = new(NullLogger<HelpContentService>.Instance);

        HelpContent first = await service.GetAsync(CancellationToken.None).ConfigureAwait(true);
        HelpContent second = await service.GetAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetAsync_TocContainsHumanReadableTitles()
    {
        using HelpContentService service = new(NullLogger<HelpContentService>.Instance);

        HelpContent content = await service.GetAsync(CancellationToken.None).ConfigureAwait(true);

        HelpTocEntry installEntry = content.Toc.First(entry => entry.Slug == HelpContext.Install);
        Assert.Contains("Installation", installEntry.Title, StringComparison.OrdinalIgnoreCase);
    }
}
