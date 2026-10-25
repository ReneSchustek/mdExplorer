using MdExplorer.Core.Abstractions;
using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;
using MdExplorer.Search.Options;
using MdExplorer.Search.Services;
using MdExplorer.Search.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MdExplorer.Search.Tests.Services;

/// <summary>
/// Der <see cref="Fts5SearchService"/> leitet Suchen im RegEx-Modus durch den Nachfilter über den
/// Fließtext und Ähnlichkeitssuchen durch den passenden Builder, unabhängig von SQLite. Eine falsche
/// Weiche lieferte im RegEx-Modus jeden Vorfilter-Treffer, ob das Muster passt oder nicht.
/// </summary>
public sealed class Fts5SearchServiceRegexAndSimilarityTests
{
    private static readonly Guid IdAlpha = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid IdBeta = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task SearchAsync_RegexMode_FiltersCandidatesByCompiledRegex()
    {
        FakeSearchIndexStorage storage = new();
        storage.AddHit(IdAlpha, "alpha.md", "Alpha", "Dieser Text enthält API123Main.");
        storage.AddHit(IdBeta, "beta.md", "Beta", "Hier nur Main, kein API.");

        Fts5SearchService sut = NewService(storage);

        IReadOnlyList<SearchResult> results = await sut
            .SearchAsync(new SearchQuery("\\bAPI\\d+Main", Mode: SearchMode.Regex), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        SearchResult single = Assert.Single(results);
        Assert.Equal(IdAlpha, single.MarkdownFileId);
        _ = Assert.Single(storage.LoadBodiesCalls);
    }

    [Fact]
    public async Task SearchAsync_RegexMode_OnPatternWithoutTokens_ReturnsEmpty()
    {
        FakeSearchIndexStorage storage = new();
        storage.AddHit(IdAlpha, "alpha.md", "Alpha", "egal");
        Fts5SearchService sut = NewService(storage);

        IReadOnlyList<SearchResult> results = await sut
            .SearchAsync(new SearchQuery(".+", Mode: SearchMode.Regex), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Empty(results);
        Assert.Empty(storage.QueryCalls);
    }

    [Fact]
    public async Task SearchAsync_RegexMode_WithoutAGuaranteedWordStart_IsRejectedWithoutQuerying()
    {
        // "ment" kann mitten im Wort stehen; ein Vorfilter darüber verschwiege "Dokument".
        FakeSearchIndexStorage storage = new();
        storage.AddHit(IdAlpha, "alpha.md", "Alpha", "Dokument");
        Fts5SearchService sut = NewService(storage);

        IReadOnlyList<SearchResult> results = await sut
            .SearchAsync(new SearchQuery("ment", Mode: SearchMode.Regex), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Empty(results);
        Assert.Empty(storage.QueryCalls);
    }

    [Fact]
    public async Task SearchAsync_RegexMode_OnInvalidPattern_ReturnsEmpty()
    {
        FakeSearchIndexStorage storage = new();
        Fts5SearchService sut = NewService(storage);

        // Ungültige RegEx-Syntax: der Compiler-Pfad fängt ArgumentException und liefert leere Trefferliste.
        IReadOnlyList<SearchResult> results = await sut
            .SearchAsync(new SearchQuery("(unclosed", Mode: SearchMode.Regex), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_Fts5Mode_WithStemmedSimilarity_UsesStemmedMatchExpression()
    {
        FakeSearchIndexStorage storage = new();
        storage.AddHit(IdAlpha, "alpha.md", "Alpha", "(Body wird im FTS5-Modus nicht verwendet)");
        Fts5SearchService sut = NewService(storage);

        IReadOnlyList<SearchResult> results = await sut
            .SearchAsync(new SearchQuery("laufen", Similarity: SimilarityMode.Stemmed), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        // Beim Speicher kommt der Ausdruck über den Wortstamm an.
        _ = Assert.Single(storage.QueryCalls);
        Assert.Equal("\"lauf\"*", storage.QueryCalls[0].MatchExpression);
        _ = Assert.Single(results);
    }

    [Fact]
    public async Task SearchAsync_RegexMode_WithTakeLimit_StopsAtTheRequestedCount()
    {
        // Ohne die Abbruchbedingung liefe der Postfilter durch alle Kandidaten, obwohl die
        // Trefferliste längst voll ist; bei weichen Mustern sind das Tausende Dokumente.
        FakeSearchIndexStorage storage = new();
        storage.AddHit(IdAlpha, "alpha.md", "Alpha", "Text mit API123Main.");
        storage.AddHit(IdBeta, "beta.md", "Beta", "Auch hier API456Main.");
        Fts5SearchService sut = NewService(storage);

        IReadOnlyList<SearchResult> results = await sut
            .SearchAsync(new SearchQuery("\\bAPI\\d+Main", Mode: SearchMode.Regex, Take: 1), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        _ = Assert.Single(results);
    }

    [Fact]
    public async Task SearchAsync_RegexMode_WithSkip_SkipsTheLeadingMatches()
    {
        FakeSearchIndexStorage storage = new();
        storage.AddHit(IdAlpha, "alpha.md", "Alpha", "Text mit API123Main.");
        storage.AddHit(IdBeta, "beta.md", "Beta", "Auch hier API456Main.");
        Fts5SearchService sut = NewService(storage);

        IReadOnlyList<SearchResult> results = await sut
            .SearchAsync(new SearchQuery("\\bAPI\\d+Main", Mode: SearchMode.Regex, Skip: 1, Take: 5), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        SearchResult single = Assert.Single(results);
        Assert.Equal(IdBeta, single.MarkdownFileId);
    }

    [Fact]
    public async Task SearchAsync_RegexMode_OnALaterPage_FetchesEnoughCandidates()
    {
        // Jedes zweite Dokument passt zum Muster. Für die dritte Seite zu fünf Treffern müssen
        // die Kandidaten auch die zehn übersprungenen Treffer abdecken; reichte der Vorfilter nur
        // für eine Seite, käme die Seite leer zurück, obwohl es genug Treffer gibt.
        FakeSearchIndexStorage storage = new();
        for (int index = 0; index < 60; index++)
        {
            string body = index % 2 == 0 ? "Text mit API123Main." : "Text ohne Treffer, nur API.";
            storage.AddHit(new Guid(index + 1, 0, 0, new byte[8]), $"datei-{index:D2}.md", $"Datei {index:D2}", body);
        }
        Fts5SearchService sut = NewService(storage);

        IReadOnlyList<SearchResult> results = await sut
            .SearchAsync(new SearchQuery("\\bAPI\\d+Main", Mode: SearchMode.Regex, Skip: 10, Take: 5), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(5, results.Count);
        Assert.Equal("datei-20.md", results[0].Path, StringComparer.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_RegexMode_WithoutAStoredBody_SkipsTheCandidate()
    {
        // Ein Kandidat ohne Fließtext kann nicht gegen das Muster geprüft werden, etwa
        // wenn die Quelldatei beim Indizieren gesperrt war.
        FakeSearchIndexStorage storage = new();
        storage.AddHit(IdAlpha, "alpha.md", "Alpha", string.Empty);
        storage.AddHit(IdBeta, "beta.md", "Beta", "Hier steht API456Main.");
        Fts5SearchService sut = NewService(storage);

        IReadOnlyList<SearchResult> results = await sut
            .SearchAsync(new SearchQuery("\\bAPI\\d+Main", Mode: SearchMode.Regex), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        SearchResult single = Assert.Single(results);
        Assert.Equal(IdBeta, single.MarkdownFileId);
    }

    [Fact]
    public async Task SearchAsync_WithoutATakeValue_FallsBackToTheConfiguredDefault()
    {
        FakeSearchIndexStorage storage = new();
        storage.AddHit(IdAlpha, "alpha.md", "Alpha", "egal");
        Fts5SearchService sut = NewService(storage);

        IReadOnlyList<SearchResult> results = await sut
            .SearchAsync(new SearchQuery("alpha", Take: 0), TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        _ = Assert.Single(results);
        Assert.Equal(new SearchOptions().DefaultTake, storage.QueryCalls[0].Take);
    }

    private static Fts5SearchService NewService(ISearchIndexStorage storage)
    {
        SearchOptions options = new();
        IOptions<SearchOptions> wrappedOptions = Microsoft.Extensions.Options.Options.Create(options);
        SearchQueryBuilder builder = new();
        SimilarityQueryBuilder similarity = new(builder, new EmptySynonymProvider(), wrappedOptions);
        return new Fts5SearchService(
            storage,
            builder,
            similarity,
            wrappedOptions,
            TimeProvider.System,
            NullLogger<Fts5SearchService>.Instance);
    }

    /// <summary>Synonymquelle ohne Einträge.</summary>
    private sealed class EmptySynonymProvider : ISynonymProvider
    {
        public IReadOnlyList<string> GetSynonyms(string stem) => [];
    }
}
