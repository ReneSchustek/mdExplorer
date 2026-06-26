using MdExplorer.Search.Models;

namespace MdExplorer.Search.Abstractions;

/// <summary>
/// Erweitert User-Eingaben um Pre-Filter (RegEx) und Similarity-Pfade (Stemming,
/// NEAR-Operator, Synonyme). Bewusst getrennt von <see cref="ISearchQueryBuilder"/>:
/// der reine FTS5-Build-Pfad und die expansionsstarken Pfade haben unterschiedliche
/// Aenderungsgruende (SRP).
/// </summary>
public interface ISimilarityQueryBuilder
{
    /// <summary>
    /// Erzeugt einen FTS5-Vorfilter aus einem RegEx-Pattern. Extrahiert
    /// alle Wort-Tokens (<c>\w{2,}</c>) und verbindet sie per <c>OR</c> mit Praefix-
    /// Wildcards. Liefert leere Match-Expression, wenn das Pattern keinen Token-Anker
    /// enthaelt (z. B. <c>.+</c>) — der Service interpretiert das als „alle Dokumente
    /// kandidieren".
    /// </summary>
    Fts5QueryPlan BuildRegexPrefilter(string regexPattern);

    /// <summary>
    /// Erzeugt einen Similarity-Match. Je nach <paramref name="mode"/> werden
    /// Anfrage-Terme gestemmt, per <c>NEAR</c> gewichtet und/oder um Synonyme erweitert.
    /// </summary>
    Fts5QueryPlan BuildSimilarity(string? userInput, SimilarityMode mode);
}
