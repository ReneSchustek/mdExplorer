using System.Text.RegularExpressions;
using MdExplorer.Core.Abstractions;
using MdExplorer.Search.Models;

namespace MdExplorer.Search.Services;

/// <summary>Ergebnis des Nachfilters.</summary>
/// <param name="Results">Die Treffer der gewünschten Seite.</param>
/// <param name="BudgetExhausted">Ob der Filter wegen des Zeitbudgets vorzeitig aufgehört hat.</param>
/// <param name="TimedOutPaths">Pfade, an denen das Einzel-Timeout gegriffen hat.</param>
internal sealed record RegexFilterOutcome(
    IReadOnlyList<SearchResult> Results,
    bool BudgetExhausted,
    IReadOnlyList<string> TimedOutPaths);

/// <summary>
/// Prüft die Kandidaten des Vorfilters mit dem regulären Ausdruck des Nutzers.
/// </summary>
/// <remarks>
/// <para>
/// Der Ausdruck wird nach Möglichkeit ohne Backtracking übersetzt
/// (<see cref="RegexOptions.NonBacktracking"/>); die Laufzeit wächst dann linear mit dem Text,
/// und kein Muster kann sich festbeißen. Rückverweise und Lookarounds kann diese Maschine nicht;
/// dann gilt die gewöhnliche mit dem Einzel-Timeout.
/// </para>
/// <para>
/// Zwei Grenzen gelten für die ganze Anfrage: der Abbruch, den die Suche bei jedem Tastendruck
/// auslöst, und ein Zeitbudget. Ohne beide liefe jede verworfene Eingabe zu Ende weiter, und das
/// Einzel-Timeout summierte sich über tausende Kandidaten zu Minuten.
/// </para>
/// </remarks>
internal sealed class RegexPostFilter(TimeProvider timeProvider, TimeSpan budget)
{
    /// <summary>Übersetzt das Muster; <see langword="null"/>, wenn es ungültig ist.</summary>
    /// <param name="pattern">Der reguläre Ausdruck.</param>
    /// <param name="matchTimeout">Das Einzel-Timeout je Text.</param>
    /// <param name="error">Die Ausnahme bei einem ungültigen Muster.</param>
    /// <returns>Der übersetzte Ausdruck.</returns>
    public static Regex? TryCompile(string pattern, TimeSpan matchTimeout, out ArgumentException? error)
    {
        error = null;
        const RegexOptions common = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
        try
        {
            return new Regex(pattern, common | RegexOptions.NonBacktracking, matchTimeout);
        }
        catch (NotSupportedException)
        {
            // Rückverweise, Lookarounds oder atomare Gruppen: nur die Maschine mit Backtracking kann das.
        }
        catch (ArgumentException ex)
        {
            error = ex;
            return null;
        }

        try
        {
            return new Regex(pattern, common, matchTimeout);
        }
        catch (ArgumentException ex)
        {
            error = ex;
            return null;
        }
    }

    /// <summary>Filtert die Kandidaten in Vorfilter-Reihenfolge.</summary>
    /// <param name="candidates">Treffer des Vorfilters, nach Relevanz sortiert.</param>
    /// <param name="bodies">Texte der Kandidaten.</param>
    /// <param name="compiled">Der übersetzte Ausdruck.</param>
    /// <param name="skip">Zu überspringende Treffer.</param>
    /// <param name="wanted">Gewünschte Treffer.</param>
    /// <param name="cancellationToken">Abbruch durch eine neue Eingabe.</param>
    /// <returns>Treffer und ob das Budget gereicht hat.</returns>
    public RegexFilterOutcome Filter(
        IReadOnlyList<SearchIndexHit> candidates,
        IReadOnlyDictionary<Guid, string> bodies,
        Regex compiled,
        int skip,
        int wanted,
        CancellationToken cancellationToken)
    {
        List<SearchResult> results = [];
        List<string> timedOut = [];
        long startedAt = timeProvider.GetTimestamp();
        int matched = 0;
        foreach (SearchIndexHit hit in candidates.TakeWhile(_ => results.Count < wanted))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (timeProvider.GetElapsedTime(startedAt) > budget)
            {
                return new RegexFilterOutcome(results, BudgetExhausted: true, timedOut);
            }

            // Übersprungen werden nur Treffer: Welcher Kandidat einer ist, weiß erst der Ausdruck.
            if (Matches(hit, bodies, compiled, timedOut) && ++matched > skip)
            {
                results.Add(new SearchResult(hit.MarkdownFileId, hit.Path, hit.Title, hit.Score, hit.Snippet));
            }
        }
        return new RegexFilterOutcome(results, BudgetExhausted: false, timedOut);
    }

    private static bool Matches(SearchIndexHit hit, IReadOnlyDictionary<Guid, string> bodies, Regex compiled, List<string> timedOut)
    {
        if (!bodies.TryGetValue(hit.MarkdownFileId, out string? body) || string.IsNullOrEmpty(body))
        {
            return false;
        }
        try
        {
            return compiled.IsMatch(body);
        }
        catch (RegexMatchTimeoutException)
        {
            timedOut.Add(hit.Path);
            return false;
        }
    }
}
