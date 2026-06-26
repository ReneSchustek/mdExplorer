using MdExplorer.App.Services.Help;

namespace MdExplorer.App.Tests.Services.Help;

/// <summary>Verifiziert das Default-/Setz-Verhalten von <see cref="HelpContextProvider"/>.</summary>
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
    public void SetSlug_TrimsLeadingAndTrailingWhitespace()
    {
        HelpContextProvider provider = new();

        provider.SetSlug("  tagcloud  ");

        Assert.Equal("tagcloud", provider.CurrentSlug);
    }
}
