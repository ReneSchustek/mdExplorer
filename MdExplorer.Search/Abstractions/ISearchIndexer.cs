namespace MdExplorer.Search.Abstractions;

/// <summary>
/// Pflegt den FTS5-Volltext-Index synchron zum Parser-Output.
/// Wird vom <c>Fts5IndexMaintainer</c>-Hosted-Service angesteuert und ist als interne
/// Modul-API gedacht. Für Tests und manuelles Re-Indexieren öffentlich.
/// </summary>
public interface ISearchIndexer
{
    /// <summary>
    /// Synchronisiert den FTS5-Index inkrementell mit den geparsten Dokumenten.
    /// Liefert die Anzahl der eingefügten/aktualisierten Datensätze.
    /// </summary>
    Task<int> SynchronizeAsync(CancellationToken cancellationToken);
}
