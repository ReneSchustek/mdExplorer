using MdExplorer.Search.Models;

namespace MdExplorer.Search.Abstractions;

/// <summary>
/// Übersetzt User-Suchanfragen in einen sicheren FTS5-MATCH-Ausdruck und zugehörige Zusatzfilter
/// (z. B. Pfad-Präfixe für die <c>UNINDEXED</c>-Spalte). Die Übersetzung muss FTS5-Injektion ausschließen
/// — reservierte Operatoren werden quotiert, Sonderzeichen werden verworfen oder eskapiert.
/// RegEx-Vorfilter und Similarity-Modi sind in <see cref="ISimilarityQueryBuilder"/> ausgelagert.
/// </summary>
public interface ISearchQueryBuilder
{
    /// <summary>Baut den Ausführungsplan für die übergebene Roh-Eingabe.</summary>
    /// <param name="userInput">Roher User-Input — darf <see langword="null"/>, leer oder Whitespace sein.</param>
    Fts5QueryPlan Build(string? userInput);
}
