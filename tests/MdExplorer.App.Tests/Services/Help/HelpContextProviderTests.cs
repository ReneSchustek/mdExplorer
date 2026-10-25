using MdExplorer.App.Services.Help;

namespace MdExplorer.App.Tests.Services.Help;

/// <summary>
/// Die Hilfe öffnet an der Seite, die der Kontext meldet. Ein leerer Wert führt zum
/// Inhaltsverzeichnis; sonst suchte die Hilfe nach einer Seite ohne Namen.
/// </summary>
public sealed class HelpContextProviderTests
{
    [Fact]
    public void CurrentSlug_OnFreshInstance_ReturnsTableOfContentsDefault()
    {
        HelpContextProvider provider = new();

        Assert.Equal(HelpContext.TableOfContents, provider.CurrentSlug);
    }

    [Fact]
    public void SetSlug_WithSpecificValue_StoresAndReturns()
    {
        HelpContextProvider provider = new();

        provider.SetSlug(HelpContext.Graph);

        Assert.Equal(HelpContext.Graph, provider.CurrentSlug);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetSlug_WithNullOrWhitespace_FallsBackToDefault(string? slug)
    {
        HelpContextProvider provider = new();
        provider.SetSlug(HelpContext.Indexing);

        provider.SetSlug(slug);

        Assert.Equal(HelpContext.TableOfContents, provider.CurrentSlug);
    }

    [Fact]
    public void SetSlug_WithSurroundingWhitespace_TrimsIt()
    {
        HelpContextProvider provider = new();

        provider.SetSlug("  tagcloud  ");

        Assert.Equal("tagcloud", provider.CurrentSlug);
    }
}
