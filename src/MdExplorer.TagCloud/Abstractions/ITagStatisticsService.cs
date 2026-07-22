using MdExplorer.TagCloud.Models;

namespace MdExplorer.TagCloud.Abstractions;

/// <summary>
/// Aggregiert Tag-Häufigkeiten über alle indizierten Markdown-Dateien. Ein Implementierungs-Aufruf
/// liefert die Top-N am häufigsten verwendeten Tags in einem einzigen SQL-Roundtrip mit
/// <c>GROUP BY</c> — kein N+1. Tags ohne Dateibezug werden gefiltert.
/// </summary>
public interface ITagStatisticsService
{
    /// <summary>
    /// Liefert die <paramref name="topN"/> häufigsten Tags absteigend nach Häufigkeit,
    /// bei Gleichstand stabil nach Slug aufsteigend.
    /// </summary>
    /// <param name="topN">Maximalanzahl zurückgegebener Tags — muss &gt; 0 sein.</param>
    /// <param name="cancellationToken">Abbruchsteuerung.</param>
    Task<IReadOnlyList<TagStatistic>> GetTopTagsAsync(int topN, CancellationToken cancellationToken);
}
