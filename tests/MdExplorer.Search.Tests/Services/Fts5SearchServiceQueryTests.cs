using MdExplorer.Core.Abstractions;
using MdExplorer.Search.Models;
using MdExplorer.Search.Options;
using MdExplorer.Search.Services;
using MdExplorer.Search.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MdExplorer.Search.Tests.Services;

/// <summary>
/// Was der Dienst aus der Anfrage an den Speicher weitergibt: Seitengröße und Pfadfilter. Ein
/// Unterstrich im Ordnernamen war im LIKE ein Platzhalter für ein beliebiges Zeichen, und ein
/// zweiter <c>path:</c>-Filter verschwand ohne Hinweis.
/// </summary>
public sealed class Fts5SearchServiceQueryTests
{
    private static readonly Guid IdAlpha = new("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task SearchAsync_WithoutAPageSize_UsesTheConfiguredDefault()
    {
        FakeSearchIndexStorage storage = WithOneHit();
        Fts5SearchService sut = NewService(storage, new SearchOptions { DefaultTake = 7 });

        _ = await sut.SearchAsync(new SearchQuery("alpha"), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(7, Assert.Single(storage.QueryCalls).Take);
    }

    [Fact]
    public async Task SearchAsync_WithTheLargestPageSizeOfTheSettings_IsNotCutShort()
    {
        // Die Einstellung „Treffer pro Seite“ reicht bis 1.000. Kappte der Dienst vorher auf eine
        // kleinere Obergrenze, wirkte die Einstellung oberhalb davon nicht.
        FakeSearchIndexStorage storage = WithOneHit();
        Fts5SearchService sut = NewService(storage);

        _ = await sut.SearchAsync(
            new SearchQuery("alpha", Take: MdExplorer.Core.Models.AppearanceSettings.MaxResultsPerPage),
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(MdExplorer.Core.Models.AppearanceSettings.MaxResultsPerPage, Assert.Single(storage.QueryCalls).Take);
    }

    [Fact]
    public async Task SearchAsync_WithAnUnderscoreInThePath_MatchesItLiterally()
    {
        FakeSearchIndexStorage storage = WithOneHit();
        Fts5SearchService sut = NewService(storage);

        _ = await sut.SearchAsync(new SearchQuery("alpha path:my_docs"), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(@"my\_docs%", Assert.Single(storage.QueryCalls).PathLikePattern);
    }

    [Fact]
    public async Task SearchAsync_WithForwardSlashes_SearchesTheStoredBackslashPaths()
    {
        // Gespeichert ist der relative Pfad des Betriebssystems; unter Windows mit Backslash.
        FakeSearchIndexStorage storage = WithOneHit();
        Fts5SearchService sut = NewService(storage);

        _ = await sut.SearchAsync(new SearchQuery("alpha path:notizen/2026"), TestContext.Current.CancellationToken).ConfigureAwait(true);

        string expected = OperatingSystem.IsWindows() ? @"notizen\\2026%" : "notizen/2026%";
        Assert.Equal(expected, Assert.Single(storage.QueryCalls).PathLikePattern);
    }

    [Fact]
    public async Task SearchAsync_WithNestedPathFilters_UsesTheNarrowerOne()
    {
        FakeSearchIndexStorage storage = WithOneHit();
        Fts5SearchService sut = NewService(storage);

        _ = await sut.SearchAsync(new SearchQuery("alpha path:projekte path:projekte2026"), TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal("projekte2026%", Assert.Single(storage.QueryCalls).PathLikePattern);
    }

    [Fact]
    public async Task SearchAsync_WithDisjointPathFilters_FindsNothing()
    {
        // Mehrere Filter gelten zugleich. Zwei Ordner, die einander nicht enthalten, kann keine
        // Datei gleichzeitig erfüllen.
        FakeSearchIndexStorage storage = WithOneHit();
        Fts5SearchService sut = NewService(storage);

        IReadOnlyList<SearchResult> results = await sut
            .SearchAsync(new SearchQuery("alpha path:notizen path:archiv"), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Empty(results);
        Assert.Empty(storage.QueryCalls);
    }

    private static FakeSearchIndexStorage WithOneHit()
    {
        FakeSearchIndexStorage storage = new();
        storage.AddHit(IdAlpha, "alpha.md", "Alpha", "alpha");
        return storage;
    }

    private static Fts5SearchService NewService(ISearchIndexStorage storage, SearchOptions? options = null)
    {
        IOptions<SearchOptions> wrappedOptions = Microsoft.Extensions.Options.Options.Create(options ?? new SearchOptions());
        SearchQueryBuilder builder = new();
        SimilarityQueryBuilder similarity = new(builder, new NoSynonyms(), wrappedOptions);
        return new Fts5SearchService(storage, builder, similarity, wrappedOptions, TimeProvider.System, NullLogger<Fts5SearchService>.Instance);
    }

    /// <summary>Synonymquelle ohne Einträge.</summary>
    private sealed class NoSynonyms : MdExplorer.Search.Abstractions.ISynonymProvider
    {
        public IReadOnlyList<string> GetSynonyms(string stem) => [];
    }
}
