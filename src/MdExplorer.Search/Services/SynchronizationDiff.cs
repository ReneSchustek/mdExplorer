using MdExplorer.Core.Abstractions;

namespace MdExplorer.Search.Services;

/// <summary>
/// Was ein Abgleich zu tun hat: neue oder geänderte Dokumente bis zur Stapelgrenze und alle
/// Index-Einträge, zu denen es kein Dokument mehr gibt.
/// </summary>
/// <param name="Targets">Neue oder geänderte Dokumente bis zur Stapelgrenze.</param>
/// <param name="Orphans">Index-Einträge ohne Dokument.</param>
/// <param name="LiveIds">Alle Dokumente des Bestands.</param>
internal readonly record struct SynchronizationDiff(
    IReadOnlyList<SearchSourceDocument> Targets,
    IReadOnlyList<Guid> Orphans,
    IReadOnlySet<Guid> LiveIds);
