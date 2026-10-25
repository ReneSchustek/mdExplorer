using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.App.Services;

/// <summary>
/// Gibt der Tag-Cloud je Abfrage einen eigenen Scope. Die Tag-Cloud lebt so lange wie die
/// Anwendung, die Statistik dahinter hängt an einem DbContext. Der ist nicht threadsicher; zwei
/// überlappende Aktualisierungen auf einem gemeinsamen Kontext brächen mit „A second operation
/// was started on this context“ ab.
/// </summary>
internal sealed class ScopedTagStatisticsService(IServiceScopeFactory scopeFactory) : ITagStatisticsService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TagStatistic>> GetTopTagsAsync(int topN, CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            ITagStatisticsService statistics = scope.ServiceProvider.GetRequiredService<ITagStatisticsService>();
            return await statistics.GetTopTagsAsync(topN, cancellationToken).ConfigureAwait(false);
        }
    }
}
