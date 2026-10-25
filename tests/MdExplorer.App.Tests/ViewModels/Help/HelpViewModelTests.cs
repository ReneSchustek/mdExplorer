using MdExplorer.App.Services.Help;
using MdExplorer.App.ViewModels.Help;

namespace MdExplorer.App.Tests.ViewModels.Help;

/// <summary>
/// Prüft das Inhaltsverzeichnis des Hilfefensters. Es wird bei jedem Öffnen neu gesetzt;
/// bliebe der alte Bestand stehen, zeigte die Liste Kapitel doppelt an.
/// </summary>
public sealed class HelpViewModelTests
{
    [Fact]
    public void SetToc_FillsTheTableOfContents()
    {
        HelpViewModel sut = new();

        sut.SetToc([new HelpTocEntry("start", "Erste Schritte"), new HelpTocEntry("suche", "Suchen")]);

        Assert.Equal(2, sut.Toc.Count);
        Assert.Equal("start", sut.Toc[0].Slug, StringComparer.Ordinal);
        Assert.Equal("Suchen", sut.Toc[1].Title, StringComparer.Ordinal);
    }

    [Fact]
    public void SetToc_CalledAgain_ReplacesTheEntriesInsteadOfAppending()
    {
        HelpViewModel sut = new();
        sut.SetToc([new HelpTocEntry("start", "Erste Schritte")]);

        sut.SetToc([new HelpTocEntry("suche", "Suchen")]);

        HelpTocEntry single = Assert.Single(sut.Toc);
        Assert.Equal("suche", single.Slug, StringComparer.Ordinal);
    }

    [Fact]
    public void SetToc_WithAnEmptyList_ClearsTheTableOfContents()
    {
        HelpViewModel sut = new();
        sut.SetToc([new HelpTocEntry("start", "Erste Schritte")]);

        sut.SetToc([]);

        Assert.Empty(sut.Toc);
    }

    [Fact]
    public void SetToc_WithoutEntries_Throws()
    {
        HelpViewModel sut = new();

        _ = Assert.Throws<ArgumentNullException>(() => sut.SetToc(null!));
    }

    [Fact]
    public void SelectedEntry_WhenChanged_RaisesPropertyChanged()
    {
        HelpViewModel sut = new();
        List<string?> reported = [];
        sut.PropertyChanged += (_, e) => reported.Add(e.PropertyName);

        sut.SelectedEntry = new HelpTocEntry("suche", "Suchen");

        Assert.Contains(nameof(HelpViewModel.SelectedEntry), reported, StringComparer.Ordinal);
        Assert.Equal("suche", sut.SelectedEntry!.Slug, StringComparer.Ordinal);
    }

    [Fact]
    public void FindEntry_KnownSlug_ReturnsItsEntry()
    {
        HelpViewModel sut = new();
        sut.SetToc([new HelpTocEntry("start", "Erste Schritte"), new HelpTocEntry("suche", "Suchen")]);

        Assert.Equal("Suchen", sut.FindEntry("suche")?.Title);
    }

    [Fact]
    public void FindEntry_UnknownSlug_ReturnsNothing()
    {
        HelpViewModel sut = new();
        sut.SetToc([new HelpTocEntry("start", "Erste Schritte")]);

        Assert.Null(sut.FindEntry("graph"));
    }

    [Theory]
    [InlineData(0, -1, "0 / 0")]
    [InlineData(7, -1, "1 / 7")]
    [InlineData(7, 0, "1 / 7")]
    [InlineData(7, 6, "7 / 7")]
    public void ShowHits_CountsFromOne(int count, int index, string expected)
    {
        HelpViewModel sut = new();

        sut.ShowHits(count, index);

        Assert.Equal(expected, sut.HitSummary, StringComparer.Ordinal);
    }
}
