using MdExplorer.App.Services.Help;
using MdExplorer.App.ViewModels.Help;

namespace MdExplorer.App.Tests.ViewModels.Help;

/// <summary>
/// Prüft das Inhaltsverzeichnis des Hilfefensters. Es wird bei jedem Öffnen neu gesetzt —
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

        HelpTocEntry einziger = Assert.Single(sut.Toc);
        Assert.Equal("suche", einziger.Slug, StringComparer.Ordinal);
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
        List<string?> gemeldet = [];
        sut.PropertyChanged += (_, e) => gemeldet.Add(e.PropertyName);

        sut.SelectedEntry = new HelpTocEntry("suche", "Suchen");

        Assert.Contains(nameof(HelpViewModel.SelectedEntry), gemeldet, StringComparer.Ordinal);
        Assert.Equal("suche", sut.SelectedEntry!.Slug, StringComparer.Ordinal);
    }
}
