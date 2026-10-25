using System.Text.RegularExpressions;
using MdExplorer.Core.Abstractions;
using MdExplorer.Search.Services;

namespace MdExplorer.Search.Tests.Services;

/// <summary>
/// Der Nachfilter der RegEx-Suche hört auf, wenn eine neue Eingabe die alte überholt oder das
/// Zeitbudget der Anfrage verbraucht ist. Vorher lief jede verworfene Eingabe zu Ende, und das
/// Einzel-Timeout summierte sich über tausende Kandidaten zu Minuten.
/// </summary>
public sealed class RegexPostFilterTests
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(200);

    [Fact]
    public void TryCompile_ForAnOrdinaryPattern_UsesTheEngineWithoutBacktracking()
    {
        Regex? compiled = RegexPostFilter.TryCompile(@"\b(a+)+b", MatchTimeout, out ArgumentException? error);

        Assert.Null(error);
        Assert.NotNull(compiled);
        Assert.True(compiled.Options.HasFlag(RegexOptions.NonBacktracking));
    }

    [Fact]
    public void TryCompile_WithABackReference_FallsBackToTheBacktrackingEngine()
    {
        Regex? compiled = RegexPostFilter.TryCompile(@"\b(\w+) \1\b", MatchTimeout, out ArgumentException? error);

        Assert.Null(error);
        Assert.NotNull(compiled);
        Assert.False(compiled.Options.HasFlag(RegexOptions.NonBacktracking));
    }

    [Fact]
    public void TryCompile_ForAnInvalidPattern_ReportsTheError()
    {
        Regex? compiled = RegexPostFilter.TryCompile("(offen", MatchTimeout, out ArgumentException? error);

        Assert.Null(compiled);
        Assert.NotNull(error);
    }

    [Fact]
    public void Filter_WhenCancelled_StopsInsteadOfFinishing()
    {
        RegexPostFilter sut = new(TimeProvider.System, TimeSpan.FromMinutes(1));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        _ = Assert.Throws<OperationCanceledException>(
            () => sut.Filter(Candidates(3), Bodies(3), Compile("wort"), skip: 0, wanted: 10, cancellation.Token));
    }

    [Fact]
    public void Filter_WhenTheBudgetIsSpent_ReturnsWhatItFoundSoFar()
    {
        // Jede Zeitabfrage rückt die Uhr eine Sekunde vor; das Budget von zweieinhalb Sekunden
        // reicht damit für zwei Kandidaten.
        RegexPostFilter sut = new(new SteppingTimeProvider(TimeSpan.FromSeconds(1)), TimeSpan.FromSeconds(2.5));

        RegexFilterOutcome outcome = sut.Filter(Candidates(5), Bodies(5), Compile("wort"), skip: 0, wanted: 10, CancellationToken.None);

        Assert.True(outcome.BudgetExhausted);
        Assert.Equal(2, outcome.Results.Count);
    }

    [Fact]
    public void Filter_SkipsOnlyMatchingCandidates()
    {
        RegexPostFilter sut = new(TimeProvider.System, TimeSpan.FromMinutes(1));
        List<SearchIndexHit> candidates = Candidates(4);
        Dictionary<Guid, string> bodies = Bodies(4);
        bodies[candidates[0].MarkdownFileId] = "nichts";

        RegexFilterOutcome outcome = sut.Filter(candidates, bodies, Compile("wort"), skip: 1, wanted: 10, CancellationToken.None);

        Assert.Equal([candidates[2].MarkdownFileId, candidates[3].MarkdownFileId], outcome.Results.Select(result => result.MarkdownFileId));
    }

    private static Regex Compile(string pattern) =>
        RegexPostFilter.TryCompile(pattern, MatchTimeout, out _)!; // Muster aus dem Test, gültig

    private static List<SearchIndexHit> Candidates(int count) =>
        [.. Enumerable.Range(1, count).Select(index => new SearchIndexHit(IdOf(index), $"d{index}.md", $"D{index}", string.Empty, 0.0))];

    private static Dictionary<Guid, string> Bodies(int count) =>
        Enumerable.Range(1, count).ToDictionary(IdOf, index => $"ein wort Nummer {index}");

    private static Guid IdOf(int index) => new(index, 0, 0, new byte[8]);

    /// <summary>Uhr, die bei jeder Zeitabfrage um einen festen Schritt weiterläuft.</summary>
    private sealed class SteppingTimeProvider(TimeSpan step) : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            _ticks += step.Ticks;
            return _ticks;
        }
    }
}
