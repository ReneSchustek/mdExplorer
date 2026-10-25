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
/// Ausführung an <see cref="ISearchIndexStorage"/>. Mehrere <c>path:</c>-Filter gelten zugleich
/// (<see cref="PathFilterPattern"/>). RegEx-Modus filtert die
/// FTS5-Vorfilter-Treffer per <see cref="Regex"/> nach. Similarity-Modi erweitern
/// den Match-Ausdruck per Stemming / NEAR / Synonymen.
/// </summary>
public sealed partial class Fts5SearchService : ISearchService
{
    // Kandidaten je gewünschtem Treffer im RegEx-Modus. Der Wert ist gesetzt, nicht gemessen: Er
    // nimmt an, dass der Ausdruck höchstens drei von vier Wort-Treffern verwirft.
    private const int RegexCandidateFactor = 4;

    private readonly ISearchIndexStorage _storage;
    private readonly ISearchQueryBuilder _queryBuilder;
    private readonly ISimilarityQueryBuilder _similarityBuilder;
    private readonly SearchOptions _options;
    private readonly RegexPostFilter _postFilter;
    private readonly ILogger<Fts5SearchService> _logger;

    /// <summary>Erzeugt den Such-Service und löst Abhängigkeiten auf.</summary>
    /// <param name="storage">Der FTS5-Speicher.</param>
    /// <param name="queryBuilder">Übersetzt die Eingabe in MATCH.</param>
    /// <param name="similarityBuilder">Vorfilter und Ähnlichkeitsmodi.</param>
    /// <param name="options">Grenzen und Gewichte der Suche.</param>
    /// <param name="timeProvider">Uhr für das Zeitbudget des RegEx-Nachfilters.</param>
    /// <param name="logger">Protokoll.</param>
    public Fts5SearchService(
        ISearchIndexStorage storage,
        ISearchQueryBuilder queryBuilder,
        ISimilarityQueryBuilder similarityBuilder,
        IOptions<SearchOptions> options,
        TimeProvider timeProvider,
        ILogger<Fts5SearchService> logger)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(queryBuilder);
        ArgumentNullException.ThrowIfNull(similarityBuilder);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _storage = storage;
        _queryBuilder = queryBuilder;
        _similarityBuilder = similarityBuilder;
        _options = options.Value;
        _postFilter = new RegexPostFilter(timeProvider, TimeSpan.FromMilliseconds(_options.RegexScanBudgetMs));
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

    private static SearchResult ToResult(SearchIndexHit hit) =>
        new(hit.MarkdownFileId, hit.Path, hit.Title, hit.Score, hit.Snippet);

    private static List<SearchResult> MapHits(IReadOnlyList<SearchIndexHit> hits)
    {
        List<SearchResult> results = new(hits.Count);
        foreach (SearchIndexHit hit in hits)
        {
            results.Add(ToResult(hit));
        }
        return results;
    }

    // Ungültige oder nicht vorfilterbare Muster entstehen beim Tippen ständig, je Entprellung
    // einmal. Als Warnung füllten sie das Protokoll; sie sind Debug-Informationen.
    [LoggerMessage(EventId = 500, Level = LogLevel.Debug, Message = "RegEx-Suche ohne gesicherten Wortanfang wird abgelehnt (Pattern: {Pattern}).")]
    private static partial void LogRegexWithoutPrefilter(ILogger logger, string pattern);

    [LoggerMessage(EventId = 501, Level = LogLevel.Warning, Message = "RegEx-Auswertung hat bei {Count} Dokument(en) das Einzel-Timeout überschritten, zuerst bei {Path}.")]
    private static partial void LogRegexTimeout(ILogger logger, int count, string path);

    [LoggerMessage(EventId = 502, Level = LogLevel.Debug, Message = "RegEx-Pattern {Pattern} ist ungültig — Suche liefert leere Treffermenge.")]
    private static partial void LogRegexCompileFailed(ILogger logger, string pattern, Exception exception);

    [LoggerMessage(EventId = 503, Level = LogLevel.Information, Message = "RegEx-Nachfilter hat das Zeitbudget ausgeschöpft; die Seite enthält die bis dahin gefundenen {Count} Treffer.")]
    private static partial void LogRegexBudgetExhausted(ILogger logger, int count);

    private async Task<IReadOnlyList<SearchResult>> SearchFts5Async(SearchQuery query, CancellationToken cancellationToken)
    {
        Fts5QueryPlan plan = query.Similarity == SimilarityMode.None
            ? _queryBuilder.Build(query.Text)
            : _similarityBuilder.BuildSimilarity(query.Text, query.Similarity);

        if (string.IsNullOrWhiteSpace(plan.MatchExpression)
            || !PathFilterPattern.TryBuild(plan.PathPrefixes, out string? pathLikePattern))
        {
            return [];
        }

        int take = NormalizeTake(query.Take);
        int skip = Math.Max(0, query.Skip);
        SearchIndexQuery indexQuery = BuildIndexQuery(plan.MatchExpression, pathLikePattern, take, skip);
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
            // Ohne gesicherten Wortanfang (etwa ".+" oder "ment") gibt es keinen verlustfreien
            // Vorfilter, nur einen Durchlauf über alle Texte. Die Anfrage wird abgelehnt, statt
            // Treffer zu verschweigen oder diesen Durchlauf auszulösen.
            LogRegexWithoutPrefilter(_logger, query.Text);
            return [];
        }

        int wanted = NormalizeTake(query.Take);
        int skip = Math.Max(0, query.Skip);
        IReadOnlyList<SearchIndexHit> candidates = await LoadCandidatesAsync(plan, skip, wanted, cancellationToken).ConfigureAwait(false);
        return candidates.Count == 0
            ? []
            : await FilterCandidatesAsync(candidates, compiled, skip, wanted, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<SearchResult>> FilterCandidatesAsync(
        IReadOnlyList<SearchIndexHit> candidates,
        Regex compiled,
        int skip,
        int wanted,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, string> bodies = await _storage
            .LoadBodiesAsync([.. candidates.Select(static c => c.MarkdownFileId)], cancellationToken)
            .ConfigureAwait(false);

        RegexFilterOutcome outcome = _postFilter.Filter(candidates, bodies, compiled, skip, wanted, cancellationToken);
        ReportOutcome(outcome);
        return outcome.Results;
    }

    // Übersprungen wird erst nach dem Ausdruck, weil nur er weiß, welcher Kandidat ein Treffer ist.
    // Der Vorfilter muss deshalb die übersprungenen Treffer mit abdecken, und das Vierfache, weil
    // der Ausdruck einen Teil der Kandidaten verwirft. Die Obergrenze bleibt: Seiten jenseits davon
    // fallen kürzer aus, statt die ganze Tabelle zu laden.
    private Task<IReadOnlyList<SearchIndexHit>> LoadCandidatesAsync(Fts5QueryPlan plan, int skip, int wanted, CancellationToken cancellationToken)
    {
        long candidatesNeeded = ((long)skip + wanted) * RegexCandidateFactor;
        int prefilterTake = (int)Math.Min(_options.MaxRegexCandidates, candidatesNeeded);
        SearchIndexQuery indexQuery = BuildIndexQuery(plan.MatchExpression, pathLikePattern: null, prefilterTake, skip: 0);
        return _storage.QueryAsync(indexQuery, cancellationToken);
    }

    private void ReportOutcome(RegexFilterOutcome outcome)
    {
        if (outcome.TimedOutPaths.Count > 0)
        {
            LogRegexTimeout(_logger, outcome.TimedOutPaths.Count, outcome.TimedOutPaths[0]);
        }
        if (outcome.BudgetExhausted)
        {
            LogRegexBudgetExhausted(_logger, outcome.Results.Count);
        }
    }

    private SearchIndexQuery BuildIndexQuery(string matchExpression, string? pathLikePattern, int take, int skip)
    {
        return new SearchIndexQuery(
            matchExpression,
            pathLikePattern,
            take,
            skip,
            _options.TitleWeight,
            _options.BodyWeight,
            _options.TagsWeight,
            _options.FrontmatterWeight,
            _options.SnippetTokenCount);
    }

    private Regex? TryCompileRegex(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }
        Regex? compiled = RegexPostFilter.TryCompile(pattern, TimeSpan.FromMilliseconds(_options.RegexTimeoutMs), out ArgumentException? error);
        if (error is not null)
        {
            LogRegexCompileFailed(_logger, pattern, error);
        }
        return compiled;
    }

    private int NormalizeTake(int requested)
    {
        if (requested <= 0)
        {
            requested = _options.DefaultTake;
        }
        return Math.Min(requested, _options.MaxResults);
    }
}
