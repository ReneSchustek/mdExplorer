using MdExplorer.App.Services.Help;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.Services.Help;

/// <summary>
/// Läuft gegen das echte, in die App-Assembly eingebettete Handbuch. Fehlt ein Slug aus
/// <see cref="HelpContext"/> im Handbuch oder sein Anker im HTML, öffnet F1 an dieser Stelle
/// nur den Anfang der Hilfe.
/// </summary>
public sealed class HelpContentServiceTests
{
    [Fact]
    public async Task GetAsync_OnTheEmbeddedManual_ReturnsTocAndHtml()
    {
        using HelpContentService service = new(NullLogger<HelpContentService>.Instance);

        HelpContent content = await service.GetAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.False(string.IsNullOrWhiteSpace(content.Html), "HTML darf nicht leer sein.");
        // Das Handbuch hat über 25.000 Zeichen; tausend trennen es sicher von einer Ressource,
        // die nur noch aus ein paar Überschriften besteht.
        Assert.True(content.PlainText.Length > 1000, "Plaintext muss substanziell sein.");
        Assert.NotEmpty(content.Toc);
        Assert.Contains(content.Toc, entry => entry.Slug == HelpContext.Install);
        Assert.Contains(content.Toc, entry => entry.Slug == HelpContext.Search);
        Assert.Contains(content.Toc, entry => entry.Slug == HelpContext.Graph);
    }

    [Fact]
    public async Task GetAsync_ForEveryTocEntry_HasAnAnchorInTheHtml()
    {
        using HelpContentService service = new(NullLogger<HelpContentService>.Instance);

        HelpContent content = await service.GetAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        foreach (HelpTocEntry entry in content.Toc)
        {
            string expectedAnchor = $"id=\"{entry.Slug}\"";
            Assert.Contains(expectedAnchor, content.Html, StringComparison.Ordinal);
        }
    }

    /// <remarks>
    /// Jede Konstante ist ein Sprungziel für F1. Gezählt werden alle per Reflexion, damit eine
    /// neue Konstante nicht stillschweigend an der Prüfung vorbeigeht.
    /// </remarks>
    [Fact]
    public async Task GetAsync_EveryHelpContext_HasAnAnchorInTheHtml()
    {
        using HelpContentService service = new(NullLogger<HelpContentService>.Instance);
        string[] slugs =
        [
            .. typeof(HelpContext).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(field => field.IsLiteral)
                .Select(field => field.GetRawConstantValue())
                .OfType<string>(),
        ];

        HelpContent content = await service.GetAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(9, slugs.Length);
        Assert.All(slugs, slug => Assert.Contains($"id=\"{slug}\"", content.Html, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAsync_CalledTwice_ReturnsSameCachedInstance()
    {
        using HelpContentService service = new(NullLogger<HelpContentService>.Instance);

        HelpContent first = await service.GetAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        HelpContent second = await service.GetAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetAsync_ForTheInstallChapter_ReturnsAReadableTitle()
    {
        using HelpContentService service = new(NullLogger<HelpContentService>.Instance);

        HelpContent content = await service.GetAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);

        HelpTocEntry installEntry = content.Toc.First(entry => entry.Slug == HelpContext.Install);
        Assert.Contains("Installation", installEntry.Title, StringComparison.OrdinalIgnoreCase);
    }
}
