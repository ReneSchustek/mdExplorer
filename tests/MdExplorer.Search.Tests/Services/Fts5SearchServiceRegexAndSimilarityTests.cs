using MdExplorer.Core.Abstractions;
using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;
using MdExplorer.Search.Options;
using MdExplorer.Search.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MdExplorer.Search.Tests.Services;

/// <summary>
/// Hammertests gegen den <see cref="Fts5SearchService"/>-Dispatch: stellen sicher,
/// dass der RegEx-Postfilter und die Similarity-Builder-Pfade
/// tatsächlich aktivieren — unabhängig vom konkreten SQLite-FTS5-Backend.
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
            .SearchAsync(new SearchQuery("API\\d+Main", Mode: SearchMode.Regex), CancellationToken.None)
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
            .SearchAsync(new SearchQuery(".+", Mode: SearchMode.Regex), CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Empty(results);
        Assert.Empty(storage.QueryCalls);
    }

    [Fact]
    public async Task SearchAsync_RegexMode_OnInvalidPattern_LogsAndReturnsEmpty()
    {
        FakeSearchIndexStorage storage = new();
        Fts5SearchService sut = NewService(storage);

        // Ungültige RegEx-Syntax — der Compiler-Pfad fängt ArgumentException und liefert leere Trefferliste.
        IReadOnlyList<SearchResult> results = await sut
            .SearchAsync(new SearchQuery("(unclosed", Mode: SearchMode.Regex), CancellationToken.None)
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
            .SearchAsync(new SearchQuery("laufen", Similarity: SimilarityMode.Stemmed), CancellationToken.None)
            .ConfigureAwait(true);

        // Service hat die Stemming-Variante an die Storage gereicht.
        _ = Assert.Single(storage.QueryCalls);
        Assert.Equal("\"lauf\"*", storage.QueryCalls[0].MatchExpression);
        _ = Assert.Single(results);
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
            NullLogger<Fts5SearchService>.Instance);
    }

    private sealed class EmptySynonymProvider : ISynonymProvider
    {
        public IReadOnlyList<string> GetSynonyms(string lemma) => [];
    }

    private sealed class FakeSearchIndexStorage : ISearchIndexStorage
    {
        private readonly Dictionary<Guid, (string Path, string Title, string Body)> _docs = new();

        public List<SearchIndexQuery> QueryCalls { get; } = [];

        public List<IReadOnlyCollection<Guid>> LoadBodiesCalls { get; } = [];

        public void AddHit(Guid id, string path, string title, string body)
        {
            _docs[id] = (path, title, body);
        }

        public Task<IReadOnlyDictionary<Guid, string>> LoadIndexedHashesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ApplyChangesAsync(
            IReadOnlyCollection<Guid> deletes,
            IReadOnlyCollection<SearchIndexEntry> upserts,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<SearchIndexHit>> QueryAsync(SearchIndexQuery query, CancellationToken cancellationToken)
        {
            QueryCalls.Add(query);
            List<SearchIndexHit> hits = [];
            foreach (KeyValuePair<Guid, (string Path, string Title, string Body)> entry in _docs)
            {
                hits.Add(new SearchIndexHit(entry.Key, entry.Value.Path, entry.Value.Title, string.Empty, 0.0));
            }
            return Task.FromResult<IReadOnlyList<SearchIndexHit>>(hits);
        }

        public Task<IReadOnlyDictionary<Guid, string>> LoadBodiesAsync(
            IReadOnlyCollection<Guid> markdownFileIds,
            CancellationToken cancellationToken)
        {
            LoadBodiesCalls.Add(markdownFileIds);
            Dictionary<Guid, string> bodies = new();
            foreach (Guid id in markdownFileIds)
            {
                if (_docs.TryGetValue(id, out (string Path, string Title, string Body) doc))
                {
                    bodies[id] = doc.Body;
                }
            }
            return Task.FromResult<IReadOnlyDictionary<Guid, string>>(bodies);
        }
    }
}
