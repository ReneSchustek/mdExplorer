using CommunityToolkit.Mvvm.Messaging;
using System.IO;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.Core.Messaging;
using MdExplorer.Search.Models;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Schnelles Tippen löst genau eine Suche mit dem letzten Text aus, die Treffer erscheinen in der
/// Reihenfolge des Suchdienstes, und Seitengröße und Ordnerbeschränkung gehen in die Anfrage ein,
/// statt hinterher im Speicher zu filtern.
/// </summary>
public sealed class SearchViewModelTests
{
    private static readonly Guid FirstId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondId = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void RapidInput_SearchesOnceWithTheLastText()
    {
        using SearchTestHarness harness = new();

        harness.ViewModel.QueryText = "f";
        harness.ViewModel.QueryText = "fo";
        harness.ViewModel.QueryText = "foo";
        harness.Elapse();

        Assert.Equal(1, harness.Recording.CallCount);
        Assert.Equal("foo", harness.Recording.ReceivedQueries[0].Text);
    }

    [Fact]
    public void BeforeTheWaitTimeHasPassed_NothingIsSearched()
    {
        using SearchTestHarness harness = new();

        harness.ViewModel.QueryText = "foo";
        harness.Clock.Advance(SearchTestHarness.Debounce - TimeSpan.FromMilliseconds(1));

        Assert.Equal(0, harness.Recording.CallCount);
    }

    [Fact]
    public void Results_AppearInTheOrderOfTheSearchService()
    {
        using SearchTestHarness harness = new();
        harness.Recording.SetNextResults(
        [
            new SearchResult(FirstId, "A.md", "A", 1.0, "snippet A"),
            new SearchResult(SecondId, "B.md", "B", 2.0, "snippet B"),
        ]);

        harness.Type("foo");

        Assert.Equal(2, harness.ViewModel.Results.Count);
        Assert.Equal(FirstId, harness.ViewModel.Results[0].MarkdownFileId);
        Assert.Equal(SecondId, harness.ViewModel.Results[1].MarkdownFileId);
    }

    [Fact]
    public void TheQuery_AsksForAsManyHitsAsTheSettingsShow()
    {
        // Ohne Angabe lieferte der Dienst seine Vorgabe von 20 Treffern; die Einstellung
        // „Treffer pro Seite“ hatte keine Wirkung.
        using SearchTestHarness harness = new();

        harness.Type("foo");

        Assert.Equal(SearchTestHarness.ResultsPerPage, harness.Recording.ReceivedQueries[0].Take);
    }

    [Fact]
    public void AChangedPageSize_AppliesToTheNextQuery()
    {
        using SearchTestHarness harness = new();
        _ = harness.Messenger.Send(new SettingsChangedMessage(
            SearchTestHarness.Settings(SearchTestHarness.Debounce, SearchTestHarness.ResultsPerPage),
            SearchTestHarness.Settings(SearchTestHarness.Debounce, 200)));

        harness.Type("foo");

        Assert.Equal(200, harness.Recording.ReceivedQueries[0].Take);
    }

    [Fact]
    public void WithTheFolderScopeOn_TheFolderIsPartOfTheQuery()
    {
        // Gefiltert wurde früher erst nach der Antwort, unter den global besten Treffern. Ein
        // Ordner, dessen Treffer weiter hinten lagen, meldete dann „keine Treffer“.
        using SearchTestHarness harness = new();
        harness.ViewModel.PathPrefixFilter = "projekte" + Path.DirectorySeparatorChar;
        harness.ViewModel.ScopeToSelectedFolder = true;

        harness.Type("foo");

        string expectedFilter = "path:\"projekte" + Path.DirectorySeparatorChar + "\"";
        Assert.Equal("foo " + expectedFilter, harness.Recording.ReceivedQueries[^1].Text);
        Assert.Equal("foo", harness.ViewModel.QueryText);
    }

    [Fact]
    public void WithTheFolderScopeOff_TheSearchStaysGlobal()
    {
        using SearchTestHarness harness = new();
        harness.ViewModel.PathPrefixFilter = "projekte" + Path.DirectorySeparatorChar;

        harness.Type("foo");

        Assert.Equal("foo", harness.Recording.ReceivedQueries[0].Text);
    }

    [Fact]
    public void ChoosingAnotherFolder_WithTheScopeOn_SearchesAgain()
    {
        using SearchTestHarness harness = new();
        harness.ViewModel.ScopeToSelectedFolder = true;
        harness.ViewModel.PathPrefixFilter = "alt" + Path.DirectorySeparatorChar;
        harness.Type("foo");
        int before = harness.Recording.CallCount;

        harness.ViewModel.PathPrefixFilter = "neu" + Path.DirectorySeparatorChar;
        harness.Elapse();

        Assert.Equal(before + 1, harness.Recording.CallCount);
        Assert.Contains("neu", harness.Recording.ReceivedQueries[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ChoosingAnotherFolder_WithTheScopeOff_DoesNotSearch()
    {
        using SearchTestHarness harness = new();
        harness.Type("foo");
        int before = harness.Recording.CallCount;

        harness.ViewModel.PathPrefixFilter = "neu" + Path.DirectorySeparatorChar;
        harness.Elapse();

        Assert.Equal(before, harness.Recording.CallCount);
    }

    [Fact]
    public void AnAnswerArrivingAfterTheFieldWasCleared_IsDropped()
    {
        // Die Antwort stellt sich am UI-Thread an. Leert der Nutzer das Feld, bevor sie dran
        // ist, gehört sie zu einer Anfrage, die es nicht mehr gibt.
        QueuedUiDispatcher dispatcher = new();
        FakeSearchService service = new();
        service.SetNextResults([new SearchResult(FirstId, "A.md", "A", 1.0, "x")]);
        using SearchTestHarness harness = new(service, dispatcher);

        harness.Type("foo");
        harness.ViewModel.Clear();
        dispatcher.RunPending();

        Assert.Empty(harness.ViewModel.Results);
        Assert.True(harness.ViewModel.ShowsNothingSearchedYet);
    }

    [Fact]
    public void AnAnswerToAnOlderQuery_DoesNotReplaceTheNewerOne()
    {
        QueuedUiDispatcher dispatcher = new();
        FakeSearchService service = new();
        using SearchTestHarness harness = new(service, dispatcher);

        service.SetNextResults([new SearchResult(FirstId, "A.md", "A", 1.0, "x")]);
        harness.Type("alt");
        service.SetNextResults([new SearchResult(SecondId, "B.md", "B", 1.0, "x")]);
        harness.Type("neu");
        dispatcher.RunPending();

        _ = Assert.Single(harness.ViewModel.Results);
        Assert.Equal(SecondId, harness.ViewModel.Results[0].MarkdownFileId);
    }

    [Fact]
    public void AfterDispose_APendingTimerSearchesNothing()
    {
        SearchTestHarness harness = new();
        harness.ViewModel.QueryText = "foo";

        harness.Dispose();
        harness.Elapse();

        Assert.Equal(0, harness.Recording.CallCount);
    }
}
