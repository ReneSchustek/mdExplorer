using System.Text.RegularExpressions;
using MdExplorer.Core.Abstractions;
using MdExplorer.Search.Abstractions;
using MdExplorer.Search.Models;
using MdExplorer.Search.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MdExplorer.Search.Services;

/// <summary>
/// FTS5-basierte Implementierung von <see cref="ISearchService"/>. Übersetzt die User-Anfrage
/// über den <see cref="ISearchQueryBuilder"/> in einen FTS5-MATCH-Ausdruck und delegiert die
/// Ausführung an <see cref="ISearchIndexStorage"/>. Falls der User mehrere <c>path:</c>-Filter
/// angibt, wird zur Vereinfachung der erste verwendet. RegEx-Modus filtert die
/// FTS5-Vorfilter-Treffer per <see cref="Regex"/> nach. Similarity-Modi erweitern
/// den Match-Ausdruck per Stemming / NEAR / Synonymen.
/// </summary>
public sealed partial class Fts5SearchService : ISearchService
{
    private readonly ISearchIndexStorage _storage;
    private readonly ISearchQueryBuilder _queryBuilder;
    private readonly ISimilarityQueryBuilder _similarityBuilder;
    private readonly SearchOptions _options;
    private readonly ILogger<Fts5SearchService> _logger;

    /// <summary>Erzeugt den Such-Service und löst Abhängigkeiten auf.</summary>
    public Fts5SearchService(
        ISearchIndexStorage storage,
        ISearchQueryBuilder queryBuilder,
        ISimilarityQueryBuilder similarityBuilder,
        IOptions<SearchOptions> options,
        ILogger<Fts5SearchService> logger)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(queryBuilder);
        ArgumentNullException.ThrowIfNull(similarityBuilder);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _storage = storage;
        _queryBuilder = queryBuilder;
        _similarityBuilder = similarityBuilder;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Mode switch
        {
            SearchMode.Regex => await SearchRegexAsync(query, cancellationToken).ConfigureAwait(false),
            _ => await SearchFts5Async(query, cancellationToken).ConfigureAwait(false),
        };
    }

    private async Task<IReadOnlyList<SearchResult>> SearchFts5Async(SearchQuery query, CancellationToken cancellationToken)
    {
        Fts5QueryPlan plan = query.Similarity == SimilarityMode.None
            ? _queryBuilder.Build(query.Text)
            : _similarityBuilder.BuildSimilarity(query.Text, query.Similarity);

        if (string.IsNullOrWhiteSpace(plan.MatchExpression))
        {
            return [];
        }

        int take = NormalizeTake(query.Take);
        int skip = Math.Max(0, query.Skip);
        SearchIndexQuery indexQuery = BuildIndexQuery(plan, take, skip);
        IReadOnlyList<SearchIndexHit> hits = await _storage.QueryAsync(indexQuery, cancellationToken).ConfigureAwait(false);
        return MapHits(hits);
    }

    private async Task<IReadOnlyList<SearchResult>> SearchRegexAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        Regex? compiled = TryCompileRegex(query.Text);
        if (compiled is null)
        {
            return [];
        }

        Fts5QueryPlan plan = _similarityBuilder.BuildRegexPrefilter(query.Text);
        if (string.IsNullOrWhiteSpace(plan.MatchExpression))
        {
            // Kein Wort-Token im Pattern (z. B. ".+") — RegEx ist im aktuellen FTS5-Schema nicht ohne Full-Scan adressierbar.
            // Wir lehnen die Anfrage bewusst ab, statt einen unbegrenzten Tabellen-Scan auszulösen.
            LogRegexWithoutPrefilter(_logger, query.Text);
            return [];
        }

        int prefilterTake = Math.Min(_options.MaxRegexCandidates, NormalizeTake(query.Take) * 4);
        SearchIndexQuery indexQuery = BuildIndexQuery(plan, prefilterTake, skip: 0);
        IReadOnlyList<SearchIndexHit> candidates = await _storage.QueryAsync(indexQuery, cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return [];
        }

        IReadOnlyDictionary<Guid, string> bodies = await _storage
            .LoadBodiesAsync([.. candidates.Select(static c => c.MarkdownFileId)], cancellationToken)
            .ConfigureAwait(false);

        int wanted = NormalizeTake(query.Take);
        int skip = Math.Max(0, query.Skip);
        return FilterByRegex(candidates, bodies, compiled, skip, wanted);
    }

    private SearchIndexQuery BuildIndexQuery(Fts5QueryPlan plan, int take, int skip)
    {
        string? pathPrefix = plan.PathPrefixes.Count > 0 ? plan.PathPrefixes[0] + "%" : null;
        return new SearchIndexQuery(
            plan.MatchExpression,
            pathPrefix,
            take,
            skip,
            _options.TitleWeight,
            _options.BodyWeight,
            _options.TagsWeight,
            _options.FrontmatterWeight,
            _options.SnippetTokenCount);
    }

    private static List<SearchResult> MapHits(IReadOnlyList<SearchIndexHit> hits)
    {
        List<SearchResult> results = new(hits.Count);
        foreach (SearchIndexHit hit in hits)
        {
            IReadOnlyList<SearchHighlight> highlights = SnippetExtractor.Extract(hit.Snippet);
            results.Add(new SearchResult(hit.MarkdownFileId, hit.Path, hit.Title, hit.Score, hit.Snippet, highlights));
        }
        return results;
    }

    private List<SearchResult> FilterByRegex(
        IReadOnlyList<SearchIndexHit> candidates,
        IReadOnlyDictionary<Guid, string> bodies,
        Regex compiled,
        int skip,
        int wanted)
    {
        List<SearchResult> filtered = [];
        int skipped = 0;
        foreach (SearchIndexHit hit in candidates)
        {
            if (filtered.Count >= wanted)
            {
                break;
            }
            if (!bodies.TryGetValue(hit.MarkdownFileId, out string? body) || string.IsNullOrEmpty(body))
            {
                continue;
            }
            try
            {
                if (!compiled.IsMatch(body))
                {
                    continue;
                }
            }
            catch (RegexMatchTimeoutException ex)
            {
                LogRegexTimeout(_logger, hit.Path, ex);
                continue;
            }
            if (skipped < skip)
            {
                skipped++;
                continue;
            }
            IReadOnlyList<SearchHighlight> highlights = SnippetExtractor.Extract(hit.Snippet);
            filtered.Add(new SearchResult(hit.MarkdownFileId, hit.Path, hit.Title, hit.Score, hit.Snippet, highlights));
        }
        return filtered;
    }

    private Regex? TryCompileRegex(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }
        try
        {
            TimeSpan timeout = TimeSpan.FromMilliseconds(_options.RegexTimeoutMs);
            return new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, timeout);
        }
        catch (ArgumentException ex)
        {
            LogRegexCompileFailed(_logger, pattern, ex);
            return null;
        }
    }

    private int NormalizeTake(int requested)
    {
        if (requested <= 0)
        {
            requested = _options.DefaultTake;
        }
        return Math.Min(requested, _options.MaxResults);
    }

    [LoggerMessage(EventId = 500, Level = LogLevel.Warning, Message = "RegEx-Suche ohne extrahierbares Wort-Token wird abgelehnt (Pattern: {Pattern}).")]
    private static partial void LogRegexWithoutPrefilter(ILogger logger, string pattern);

    [LoggerMessage(EventId = 501, Level = LogLevel.Warning, Message = "RegEx-Auswertung im Body von {Path} hat den Timeout überschritten.")]
    private static partial void LogRegexTimeout(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 502, Level = LogLevel.Warning, Message = "RegEx-Pattern {Pattern} ist ungültig — Suche liefert leere Treffermenge.")]
    private static partial void LogRegexCompileFailed(ILogger logger, string pattern, Exception exception);
}
