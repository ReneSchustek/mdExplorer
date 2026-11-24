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
