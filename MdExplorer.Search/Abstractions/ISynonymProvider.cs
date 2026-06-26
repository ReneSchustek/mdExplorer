namespace MdExplorer.Search.Abstractions;

/// <summary>
/// Liefert Synonyme für eine Anfrage-Lemma. Wird im
/// <see cref="MdExplorer.Search.Models.SimilarityMode.NearStemSynonyms"/>-Modus genutzt.
/// </summary>
public interface ISynonymProvider
{
    /// <summary>
    /// Liefert die Synonyme zum (kleingeschriebenen) Stamm. Leere Liste, wenn keine vorhanden.
    /// </summary>
    IReadOnlyList<string> GetSynonyms(string lemma);
}
