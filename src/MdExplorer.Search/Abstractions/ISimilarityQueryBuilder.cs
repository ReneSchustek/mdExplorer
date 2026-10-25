using MdExplorer.Search.Models;

namespace MdExplorer.Search.Abstractions;

/// <summary>
/// Erweitert User-Eingaben um Pre-Filter (RegEx) und Similarity-Pfade (Stemming,
/// NEAR-Operator, Synonyme). Bewusst getrennt von <see cref="ISearchQueryBuilder"/>:
/// der reine FTS5-Build-Pfad und die expansionsstarken Pfade haben unterschiedliche
/// Änderungsgründe (SRP).
/// </summary>
public interface ISimilarityQueryBuilder
{
    /// <summary>
    /// Erzeugt einen FTS5-Vorfilter aus einem RegEx-Pattern: die Wortanfänge, die in jedem
    /// Treffer an einer Wortgrenze stehen müssen, als Präfixe mit <c>AND</c> verbunden. Gibt es
    /// keinen solchen Wortanfang (etwa bei <c>.+</c>, <c>ment</c> oder <c>a|b</c>), bleibt der
    /// Ausdruck leer; der Dienst lehnt die Suche dann ab, statt Treffer zu verschweigen oder die
    /// ganze Tabelle zu lesen.
    /// </summary>
    /// <param name="regexPattern">Der reguläre Ausdruck des Nutzers.</param>
    /// <returns>Der Vorfilter.</returns>
    Fts5QueryPlan BuildRegexPrefilter(string regexPattern);

    /// <summary>
    /// Erzeugt einen Similarity-Match. Je nach <paramref name="mode"/> werden
    /// Anfrage-Terme gestemmt, per <c>NEAR</c> gewichtet und/oder um Synonyme erweitert.
    /// </summary>
    Fts5QueryPlan BuildSimilarity(string? userInput, SimilarityMode mode);
}
