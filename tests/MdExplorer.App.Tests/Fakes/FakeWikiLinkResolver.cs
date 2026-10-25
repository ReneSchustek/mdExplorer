using MdExplorer.Graph.Abstractions;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>
/// Löst WikiLink-Ziele aus einer festen Zuordnung auf. Wie die Regel selbst arbeitet, prüfen die
/// Graph-Tests; hier geht es nur darum, dass der Klick sie benutzt.
/// </summary>
internal sealed class FakeWikiLinkResolver : IWikiLinkResolver
{
    private readonly Dictionary<string, Guid> _targets = new(StringComparer.Ordinal);

    public int Calls { get; private set; }

    public void Map(string wikiLinkTarget, Guid markdownFileId) => _targets[wikiLinkTarget] = markdownFileId;

    public Task<Guid?> ResolveAsync(string wikiLinkTarget, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult<Guid?>(_targets.TryGetValue(wikiLinkTarget, out Guid id) ? id : null);
    }
}
