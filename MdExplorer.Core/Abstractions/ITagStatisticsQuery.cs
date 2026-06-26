namespace MdExplorer.Core.Abstractions;

/// <summary>
/// Liefert aggregierte Tag-Statistiken aus der Persistenzschicht. Implementierung liegt in
/// der Data-Schicht (EF-Core), damit das TagCloud-Modul EF-frei bleibt — analoges Pattern
/// zu <see cref="IGraphSourceProvider"/>.
/// </summary>
public interface ITagStatisticsQuery
{
    /// <summary>
    /// Liefert die <paramref name="topN"/> häufigsten Tags absteigend nach Häufigkeit,
    /// bei Gleichstand stabil nach Slug aufsteigend. Aggregation erfolgt in einem
    /// einzigen GROUP-BY-Query (kein N+1).
    /// </summary>
    /// <param name="topN">Maximalanzahl Datensätze — muss &gt; 0 sein.</param>
    /// <param name="cancellationToken">Abbruchsteuerung.</param>
    Task<IReadOnlyList<TagStatisticRow>> GetTopTagsAsync(int topN, CancellationToken cancellationToken);
}

/// <summary>Aggregat-Zeile einer Tag-Statistik.</summary>
/// <param name="Name">Original-Tag-Name.</param>
/// <param name="Slug">Normalisierter Slug (eindeutig).</param>
/// <param name="Count">Anzahl Dateien, die den Tag tragen — strikt &gt; 0.</param>
/// <param name="LastUsedUtc">MAX(<c>MarkdownFile.LastWriteTimeUtc</c>) über alle Dateien mit diesem Tag (UTC).</param>
public sealed record TagStatisticRow(string Name, string Slug, int Count, DateTime LastUsedUtc);
