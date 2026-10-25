using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.ViewModels;
using MdExplorer.TagCloud.Messaging;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Prüft die Tag-Filter-Aufnahme und die Zustandswechsel des Such-ViewModels, also die
/// Pfade, die nicht über die Texteingabe laufen: Klicks in der Tag-Cloud, Umschalten von
/// Modus, Ähnlichkeit und Ordner-Beschränkung sowie das Leeren der Eingabe.
/// </summary>
public sealed class SearchViewModelTagFilterTests
{
    [Fact]
    public void Clear_EmptiesTheQuery()
    {
        using TestEnvironment environment = new();
        environment.ViewModel.QueryText = "irgendetwas";

        environment.ViewModel.Clear();

        Assert.Equal(string.Empty, environment.ViewModel.QueryText, StringComparer.Ordinal);
    }

    [Fact]
    public void Receive_WithoutMessage_Throws()
    {
        using TestEnvironment environment = new();

        _ = Assert.Throws<ArgumentNullException>(() => environment.ViewModel.Receive((TagClickedMessage)null!));
    }

    [Fact]
    public void Receive_WithBlankSlug_LeavesQueryUntouched()
    {
        using TestEnvironment environment = new();
        environment.ViewModel.QueryText = "unberührt";

        environment.ViewModel.Receive(new TagClickedMessage("   ", "Anzeige", TagFilterMode.Replace));

        Assert.Equal("unberührt", environment.ViewModel.QueryText, StringComparer.Ordinal);
    }

    [Fact]
    public void Receive_InReplaceMode_ReplacesTheWholeQuery()
    {
        using TestEnvironment environment = new();
        environment.ViewModel.QueryText = "alter text";

        environment.ViewModel.Receive(new TagClickedMessage("notizen", "Notizen", TagFilterMode.Replace));

        Assert.Equal("tag:notizen", environment.ViewModel.QueryText, StringComparer.Ordinal);
    }

    [Fact]
    public void Receive_InAddMode_AppendsToTheExistingQuery()
    {
        using TestEnvironment environment = new();
        environment.ViewModel.QueryText = "bericht";

        environment.ViewModel.Receive(new TagClickedMessage("notizen", "Notizen", TagFilterMode.Add));

        Assert.Equal("bericht tag:notizen", environment.ViewModel.QueryText, StringComparer.Ordinal);
    }

    [Fact]
    public void Receive_InAddMode_OnEmptyQuery_UsesTheTokenAlone()
    {
        using TestEnvironment environment = new();

        environment.ViewModel.Receive(new TagClickedMessage("notizen", "Notizen", TagFilterMode.Add));

        Assert.Equal("tag:notizen", environment.ViewModel.QueryText, StringComparer.Ordinal);
    }

    [Fact]
    public void Receive_InAddMode_DoesNotDuplicateAnExistingToken()
    {
        using TestEnvironment environment = new();
        environment.ViewModel.QueryText = "bericht tag:notizen";

        environment.ViewModel.Receive(new TagClickedMessage("notizen", "Notizen", TagFilterMode.Add));

        // Zweimal derselbe Tag ändert die Trefferliste nicht, macht die Eingabe aber unleserlich.
        Assert.Equal("bericht tag:notizen", environment.ViewModel.QueryText, StringComparer.Ordinal);
    }

    [Fact]
    public void Receive_InExcludeMode_AppendsTheNegatedToken()
    {
        using TestEnvironment environment = new();
        environment.ViewModel.QueryText = "bericht";

        environment.ViewModel.Receive(new TagClickedMessage("entwurf", "Entwurf", TagFilterMode.Exclude));

        Assert.Equal("bericht -tag:entwurf", environment.ViewModel.QueryText, StringComparer.Ordinal);
    }

    [Fact]
    public void Receive_InExcludeMode_DoesNotDuplicateAnExistingToken()
    {
        using TestEnvironment environment = new();
        environment.ViewModel.QueryText = "-tag:entwurf";

        environment.ViewModel.Receive(new TagClickedMessage("entwurf", "Entwurf", TagFilterMode.Exclude));

        Assert.Equal("-tag:entwurf", environment.ViewModel.QueryText, StringComparer.Ordinal);
    }

    [Fact]
    public void Receive_TrimsSurroundingWhitespaceOfTheExistingQuery()
    {
        using TestEnvironment environment = new();
        environment.ViewModel.QueryText = "   bericht   ";

        environment.ViewModel.Receive(new TagClickedMessage("notizen", "Notizen", TagFilterMode.Add));

        Assert.Equal("bericht tag:notizen", environment.ViewModel.QueryText, StringComparer.Ordinal);
    }

    [Fact]
    public void Receive_ViaMessenger_ReachesTheViewModel()
    {
        // Die Registrierung beim Nachrichtenverteiler ist Teil des Vertrags: Ohne sie käme
        // ein Klick in der Tag-Cloud nie an, ohne dass ein Test es merken würde.
        using TestEnvironment environment = new();

        _ = environment.Messenger.Send(new TagClickedMessage("cloud", "Cloud", TagFilterMode.Replace));

        Assert.Equal("tag:cloud", environment.ViewModel.QueryText, StringComparer.Ordinal);
    }

    [Fact]
    public void Dispose_UnregistersFromTheMessenger()
    {
        TestEnvironment environment = new();
        environment.ViewModel.Dispose();

        _ = environment.Messenger.Send(new TagClickedMessage("danach", "Danach", TagFilterMode.Replace));

        Assert.Equal(string.Empty, environment.ViewModel.QueryText, StringComparer.Ordinal);
        environment.Dispose();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        TestEnvironment environment = new();

        environment.ViewModel.Dispose();
        environment.ViewModel.Dispose();

        environment.Dispose();
    }

    [Fact]
    public void ChangingMode_TriggersANewSearch()
    {
        using SearchTestHarness harness = new();
        harness.Type("bericht");

        harness.ViewModel.Mode = MdExplorer.Search.Models.SearchMode.Regex;
        harness.Elapse();

        Assert.Equal(2, harness.Recording.CallCount);
        Assert.Equal(MdExplorer.Search.Models.SearchMode.Regex, harness.Recording.ReceivedQueries[^1].Mode);
    }

    [Fact]
    public void ChangingSimilarity_TriggersANewSearch()
    {
        using SearchTestHarness harness = new();
        harness.Type("bericht");

        harness.ViewModel.Similarity = MdExplorer.Search.Models.SimilarityMode.NearStemSynonyms;
        harness.Elapse();

        Assert.Equal(2, harness.Recording.CallCount);
        Assert.Equal(MdExplorer.Search.Models.SimilarityMode.NearStemSynonyms, harness.Recording.ReceivedQueries[^1].Similarity);
    }

    [Fact]
    public void ChangingScope_TriggersANewSearch()
    {
        using SearchTestHarness harness = new();
        harness.Type("bericht");

        harness.ViewModel.ScopeToSelectedFolder = true;
        harness.Elapse();

        Assert.Equal(2, harness.Recording.CallCount);
    }

    [Fact]
    public void ChangingMode_WithEmptyQuery_DoesNotSearch()
    {
        using SearchTestHarness harness = new();

        harness.ViewModel.Mode = MdExplorer.Search.Models.SearchMode.Regex;
        harness.Elapse();

        Assert.Equal(0, harness.Recording.CallCount);
    }

    /// <summary>Hält Dienstanbieter, Nachrichtenverteiler und ViewModel für einen Testlauf zusammen.</summary>
    private sealed class TestEnvironment : IDisposable
    {
        private readonly SearchTestHarness _harness = new();

        public StrongReferenceMessenger Messenger => _harness.Messenger;

        public SearchViewModel ViewModel => _harness.ViewModel;

        public void Dispose() => _harness.Dispose();
    }
}
