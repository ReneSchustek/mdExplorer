namespace MdExplorer.Search.Models;

/// <summary>
/// Stufen der Ähnlichkeitssuche im FTS5-Modus. Bewusst auf FTS5-Bordmittel
/// gestützt — keine Embedding-Pflicht, kein Ollama-Roundtrip.
/// </summary>
public enum SimilarityMode
{
    /// <summary>Keine Erweiterung — Default-Verhalten ohne Stemming/NEAR/Synonyme.</summary>
    None = 0,

    /// <summary>Anfrage-Terme über deutschen Suffix-Stemmer auf Stamm reduzieren und mit Präfix-Wildcards anfragen.</summary>
    Stemmed = 1,

    /// <summary>Wie <see cref="Stemmed"/>, zusätzlich werden Mehr-Wort-Anfragen über <c>NEAR</c>-Operator nachbarschaftlich gewichtet.</summary>
    NearStem = 2,

    /// <summary>Wie <see cref="NearStem"/>, zusätzlich Synonym-Erweiterung aus der konfigurierten Synonym-Datei.</summary>
    NearStemSynonyms = 3,
}
