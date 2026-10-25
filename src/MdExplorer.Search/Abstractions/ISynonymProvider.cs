namespace MdExplorer.Search.Abstractions;

/// <summary>
/// Liefert Synonyme für ein Wort der Anfrage. Wird im
/// <see cref="MdExplorer.Search.Models.SimilarityMode.NearStemSynonyms"/>-Modus genutzt.
/// </summary>
public interface ISynonymProvider
{
    /// <summary>
    /// Liefert die Synonyme zum Stamm (<see cref="MdExplorer.Search.Services.GermanStemmer"/>),
    /// ohne Rücksicht auf Groß- und Kleinschreibung. Leere Liste, wenn keine vorhanden.
    /// </summary>
    /// <param name="stem">Der Stamm eines Anfrage-Wortes.</param>
    /// <returns>Die Synonyme in Grundform.</returns>
    IReadOnlyList<string> GetSynonyms(string stem);
}
