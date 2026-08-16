using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.Tests.Fakes;
using MdExplorer.App.ViewModels;
using MdExplorer.Search.Abstractions;
using MdExplorer.TagCloud.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.ViewModels;

/// <summary>
/// Prüft die Tag-Filter-Aufnahme und die Zustandswechsel des Such-ViewModels — also die
/// Pfade, die nicht über die Texteingabe laufen: Klicks in der Tag-Cloud, Umschalten von
/// Modus, Ähnlichkeit und Ordner-Beschränkung sowie das Leeren der Eingabe.
/// </summary>
public sealed class SearchViewModelTagFilterTests
{
    private static readonly TimeSpan TestDebounce = TimeSpan.FromMilliseconds(50);

    [Fact]
    public void Clear_EmptiesTheQuery()
    {
        using TestEnvironment u = new();
        u.ViewModel.QueryText = "irgendetwas";

        u.ViewModel.Clear();

        Assert.Equal(string.Empty, u.ViewModel.QueryText, StringComparer.Ordinal);
    }

    [Fact]
    public void Receive_WithoutMessage_Throws()
    {
        using TestEnvironment u = new();

        _ = Assert.Throws<ArgumentNullException>(() => u.ViewModel.Receive(null!));
    }

    [Fact]
    public void Receive_WithBlankSlug_LeavesQueryUntouched()
    {
        using TestEnvironment u = new();
        u.ViewModel.QueryText = "unberuehrt";

        u.ViewModel.Receive(new TagClickedMessage("   ", "Anzeige", TagFilterMode.Replace));

        Assert.Equal("unberuehrt", u.ViewModel.QueryText, StringComparer.Ordinal);
    }

    [Fact]
    public void Receive_InReplaceMode_ReplacesTheWholeQuery()
    {
        using TestEnvironment u = new();
        u.ViewModel.QueryText = "alter text";

        u.ViewModel.Receive(new TagClickedMessage("notizen", "Notizen", TagFilterMode.Replace));

        Assert.Equal("tag:notizen", u.ViewModel.QueryText, StringComparer.Ordinal);
    }

    [Fact]
    public void Receive_InAddMode_AppendsToTheExistingQuery()
    {
        using TestEnvironment u = new();
        u.ViewModel.QueryText = "bericht";

        u.ViewModel.Receive(new TagClickedMessage("notizen", "Notizen", TagFilterMode.Add));

        Assert.Equal("bericht tag:notizen", u.ViewModel.QueryText, StringComparer.Ordinal);
    }

    [Fact]
    public void Receive_InAddMode_OnEmptyQuery_UsesTheTokenAlone()
    {
        using TestEnvironment u = new();

        u.ViewModel.Receive(new TagClickedMessage("notizen", "Notizen", TagFilterMode.Add));

        Assert.Equal("tag:notizen", u.ViewModel.QueryText, StringComparer.Ordinal);
    }

    [Fact]
    public void Receive_InAddMode_DoesNotDuplicateAnExistingToken()
    {
        using TestEnvironment u = new();
        u.ViewModel.QueryText = "bericht tag:notizen";

        u.ViewModel.Receive(new TagClickedMessage("notizen", "Notizen", TagFilterMode.Add));

        // Zweimal derselbe Tag ändert die Trefferliste nicht, macht die Eingabe aber unleserlich.
        Assert.Equal("bericht tag:notizen", u.ViewModel.QueryText, StringComparer.Ordinal);
    }

    [Fact]
    public void Receive_InExcludeMode_AppendsTheNegatedToken()
    {
        using TestEnvironment u = new();
        u.ViewModel.QueryText = "bericht";

        u.ViewModel.Receive(new TagClickedMessage("entwurf", "Entwurf", TagFilterMode.Exclude));

        Assert.Equal("bericht -tag:entwurf", u.ViewModel.QueryText, StringComparer.Ordinal);
    }

    [Fact]
    public void Receive_InExcludeMode_DoesNotDuplicateAnExistingToken()
    {
        using TestEnvironment u = new();
        u.ViewModel.QueryText = "-tag:entwurf";

        u.ViewModel.Receive(new TagClickedMessage("entwurf", "Entwurf", TagFilterMode.Exclude));

        Assert.Equal("-tag:entwurf", u.ViewModel.QueryText, StringComparer.Ordinal);
    }

    [Fact]
    public void Receive_TrimsSurroundingWhitespaceOfTheExistingQuery()
    {
        using TestEnvironment u = new();
        u.ViewModel.QueryText = "   bericht   ";

        u.ViewModel.Receive(new TagClickedMessage("notizen", "Notizen", TagFilterMode.Add));

        Assert.Equal("bericht tag:notizen", u.ViewModel.QueryText, StringComparer.Ordinal);
    }

    [Fact]
    public void Receive_ViaMessenger_ReachesTheViewModel()
    {
        // Die Registrierung beim Nachrichtenverteiler ist Teil des Vertrags: Ohne sie käme
        // ein Klick in der Tag-Cloud nie an, ohne dass ein Test es merken würde.
        using TestEnvironment u = new();

        _ = u.Messenger.Send(new TagClickedMessage("cloud", "Cloud", TagFilterMode.Replace));

        Assert.Equal("tag:cloud", u.ViewModel.QueryText, StringComparer.Ordinal);
    }

    [Fact]
    public void Dispose_UnregistersFromTheMessenger()
    {
        TestEnvironment u = new();
        u.ViewModel.Dispose();

        _ = u.Messenger.Send(new TagClickedMessage("danach", "Danach", TagFilterMode.Replace));

        Assert.Equal(string.Empty, u.ViewModel.QueryText, StringComparer.Ordinal);
        u.Dispose();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        TestEnvironment u = new();

        u.ViewModel.Dispose();
        u.ViewModel.Dispose();

        u.Dispose();
    }

    [Fact]
    public async Task ChangingMode_TriggersANewSearch()
    {
        using TestEnvironment u = new();
        u.ViewModel.QueryText = "bericht";
        await u.WarteAufLaufAsync().ConfigureAwait(true);
        int vorher = u.SearchService.CallCount;

        u.ViewModel.Mode = MdExplorer.Search.Models.SearchMode.Regex;
        await u.WarteAufLaufAsync().ConfigureAwait(true);

        Assert.True(u.SearchService.CallCount > vorher, "Ein Moduswechsel muss die Suche erneut auslösen.");
    }

    [Fact]
    public async Task ChangingSimilarity_TriggersANewSearch()
    {
        using TestEnvironment u = new();
        u.ViewModel.QueryText = "bericht";
        await u.WarteAufLaufAsync().ConfigureAwait(true);
        int vorher = u.SearchService.CallCount;

        u.ViewModel.Similarity = MdExplorer.Search.Models.SimilarityMode.NearStemSynonyms;
        await u.WarteAufLaufAsync().ConfigureAwait(true);

        Assert.True(u.SearchService.CallCount > vorher, "Ein Wechsel der Ähnlichkeit muss die Suche erneut auslösen.");
    }

    [Fact]
    public async Task ChangingScope_TriggersANewSearch()
    {
        using TestEnvironment u = new();
        u.ViewModel.QueryText = "bericht";
        await u.WarteAufLaufAsync().ConfigureAwait(true);
        int vorher = u.SearchService.CallCount;

        u.ViewModel.ScopeToSelectedFolder = true;
        await u.WarteAufLaufAsync().ConfigureAwait(true);

        Assert.True(u.SearchService.CallCount > vorher, "Ein Wechsel der Ordner-Beschränkung muss die Suche erneut auslösen.");
    }

    [Fact]
    public async Task ChangingMode_WithEmptyQuery_DoesNotSearch()
    {
        using TestEnvironment u = new();

        u.ViewModel.Mode = MdExplorer.Search.Models.SearchMode.Regex;
        await Task.Delay(150, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(0, u.SearchService.CallCount);
    }

    /// <summary>Hält Dienstanbieter, Nachrichtenverteiler und ViewModel für einen Testlauf zusammen.</summary>
    private sealed class TestEnvironment : IDisposable
    {
        private readonly ServiceProvider _provider;

        public TestEnvironment()
        {
            SearchService = new FakeSearchService();
            SearchService.SetNextResults([]);
            ServiceCollection services = new();
            _ = services.AddScoped<ISearchService>(_ => SearchService);
            _provider = services.BuildServiceProvider(validateScopes: true);
            Messenger = new StrongReferenceMessenger();
            ViewModel = new SearchViewModel(
                _provider.GetRequiredService<IServiceScopeFactory>(),
                TimeProvider.System,
                Messenger,
                NullLogger<SearchViewModel>.Instance,
                TestDebounce);
        }

        public FakeSearchService SearchService { get; }

        public StrongReferenceMessenger Messenger { get; }

        public SearchViewModel ViewModel { get; }

        public async Task WarteAufLaufAsync()
        {
            TaskCompletionSource fertig = new();
            void Behandler(object? sender, EventArgs e) => fertig.TrySetResult();
            ViewModel.SearchCompleted += Behandler;
            try
            {
                _ = await Task.WhenAny(fertig.Task, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
            }
            finally
            {
                ViewModel.SearchCompleted -= Behandler;
            }
        }

        public void Dispose()
        {
            ViewModel.Dispose();
            _provider.Dispose();
        }
    }
}
