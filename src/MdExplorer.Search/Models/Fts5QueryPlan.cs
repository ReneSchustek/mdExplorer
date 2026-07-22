namespace MdExplorer.Search.Models;

/// <summary>
/// Ergebnis eines <see cref="MdExplorer.Search.Abstractions.ISearchQueryBuilder"/>-Laufs.
/// Trennt die FTS5-MATCH-Expression von Filtern, die als reguläre SQL-WHERE-Klauseln umgesetzt werden müssen
/// (z. B. <c>Path</c> ist <c>UNINDEXED</c> und nicht über FTS5-MATCH adressierbar).
/// </summary>
/// <param name="MatchExpression">Validierte FTS5-MATCH-Expression. Leer, wenn keine Volltextkomponente vorhanden ist.</param>
/// <param name="PathPrefixes">Pfad-Präfix-Filter (jeweils ohne Wildcard, der Service hängt <c>%</c> für <c>LIKE</c> an).</param>
public sealed record Fts5QueryPlan(string MatchExpression, IReadOnlyList<string> PathPrefixes);
