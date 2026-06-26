using MdExplorer.Core.Abstractions;
using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.Models;

namespace MdExplorer.TagCloud.Services;

/// <summary>
/// TagCloud-fassadiger Service über <see cref="ITagStatisticsQuery"/>. Übersetzt die
/// Persistenz-neutralen <see cref="TagStatisticRow"/>-Zeilen in das modul-eigene
/// <see cref="TagStatistic"/>-Modell. Die Aggregation (GROUP BY) erfolgt in der
/// Data-Schicht; dieser Service kapselt nur die Domain-Übersetzung.
/// </summary>
public sealed class TagStatisticsService(ITagStatisticsQuery query) : ITagStatisticsService
{
    private readonly ITagStatisticsQuery _query = query ?? throw new ArgumentNullException(nameof(query));

    /// <inheritdoc />
    public async Task<IReadOnlyList<TagStatistic>> GetTopTagsAsync(int topN, CancellationToken cancellationToken)
    {
        IReadOnlyList<TagStatisticRow> rows = await _query.GetTopTagsAsync(topN, cancellationToken).ConfigureAwait(false);

        List<TagStatistic> result = new(rows.Count);
        foreach (TagStatisticRow row in rows)
        {
            result.Add(new TagStatistic(row.Name, row.Slug, row.Count, row.LastUsedUtc));
        }
        return result;
    }
}
