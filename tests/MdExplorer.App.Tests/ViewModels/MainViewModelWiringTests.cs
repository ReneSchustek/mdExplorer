using MdExplorer.App.ViewModels;
using MdExplorer.Graph.Models;
using MdExplorer.Search.Models;
using MdExplorer.Search.Services;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Die Verdrahtung zwischen den Bezügen eines Dokuments und dem Hauptfenster, geprüft, nicht angenommen.
/// </summary>
/// <remarks>
/// Vier Ereignisse verbinden die Bezüge mit dem Hauptfenster. Sie sind je zwei Zeilen lang, und
/// genau solche Zeilen fallen bei einem Umbau still weg: Der Bau bleibt grün, der Klick tut nur
/// nichts mehr.
/// </remarks>
public sealed class MainViewModelWiringTests
{
    private const int SearchTabIndex = 2;

    /// <remarks>
    /// Geprüft wird bis zum echten Übersetzer der Suche: Erst dort zeigt sich, ob die
    /// Einschränkung den Index trifft, der Pfade mit dem Trennzeichen des Systems speichert.
    /// </remarks>
    [Fact]
    public void ShowFolder_PutsAPathFilterIntoTheSearchThatMatchesTheIndex()
    {
        using MainTestHarness harness = new();
        harness.DocumentPanel.Relations.FolderPath = "projekt/mein ordner";

        harness.DocumentPanel.Relations.ShowFolderCommand.Execute(null);

        Fts5QueryPlan plan = new SearchQueryBuilder().Build(harness.Search.QueryText);
        Assert.Equal(@"projekt\mein ordner\", Assert.Single(plan.PathPrefixes), StringComparer.Ordinal);
        Assert.Equal(string.Empty, plan.MatchExpression, StringComparer.Ordinal);
        Assert.Equal(SearchTabIndex, harness.Main.LeftTabIndex);
    }

    /// <remarks>
    /// Die Gegenprobe: Ohne Ordner gibt es nichts zu zeigen. Der Befehl darf dann nicht in
    /// den Suchbereich springen und dort eine leere Einschränkung hinterlassen.
    /// </remarks>
    [Fact]
    public void ShowFolder_WithoutAFolder_ChangesNothing()
    {
        using MainTestHarness harness = new();
        int before = harness.Main.LeftTabIndex;

        harness.DocumentPanel.Relations.ShowFolderCommand.Execute(null);

        Assert.Empty(harness.Search.QueryText);
        Assert.Equal(before, harness.Main.LeftTabIndex);
    }

    [Fact]
    public void ShowTag_PutsTheTagFilterIntoTheSearchTab()
    {
        using MainTestHarness harness = new();

        harness.DocumentPanel.Relations.ShowTagCommand.Execute("architektur");

        Assert.Equal("tag:architektur", harness.Search.QueryText);
        Assert.Equal(SearchTabIndex, harness.Main.LeftTabIndex);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShowTag_WithoutATag_ChangesNothing(string? blank)
    {
        using MainTestHarness harness = new();
        int before = harness.Main.LeftTabIndex;

        harness.DocumentPanel.Relations.ShowTagCommand.Execute(blank);

        Assert.Empty(harness.Search.QueryText);
        Assert.Equal(before, harness.Main.LeftTabIndex);
    }

    [Fact]
    public void OpenRelated_OpensTheDocumentBehindTheEntry()
    {
        using MainTestHarness harness = new();
        Guid target = new("77777777-7777-7777-7777-777777777777");
        harness.Locator.SetAbsolutePath(@"C:\notizen\ziel.md", target);
        harness.DocRepo.Put(target, RenderedDocuments.Create(target, "<h1>Ziel</h1>", MainTestHarness.FixedUtc));
        RelatedDocumentViewModel entry = new(new RelatedDocument(target, "ziel", "ziel.md"));

        harness.DocumentPanel.Relations.OpenRelatedCommand.Execute(entry);

        Assert.Equal(target, harness.Preview.CurrentDocumentId);
    }

    /// <remarks>
    /// Ohne Eintrag (der Befehl kann mit <see langword="null"/> ausgelöst werden, wenn die
    /// Liste gerade leer ist) darf nichts geöffnet werden.
    /// </remarks>
    [Fact]
    public void OpenRelated_WithoutAnEntry_OpensNothing()
    {
        using MainTestHarness harness = new();

        harness.DocumentPanel.Relations.OpenRelatedCommand.Execute(null);

        Assert.Null(harness.Preview.CurrentDocumentId);
    }
}
