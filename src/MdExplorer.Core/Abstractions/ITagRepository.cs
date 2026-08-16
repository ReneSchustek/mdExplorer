using MdExplorer.Core.Models;

namespace MdExplorer.Core.Abstractions;

/// <summary>
/// Persistenz für <see cref="Tag"/> und die Join-Entität <see cref="MarkdownFileTag"/>. Implementierung in der Data-Schicht.
/// </summary>
public interface ITagRepository
{
    /// <summary>Liefert alle bestehenden Tags zu den angegebenen Slugs.</summary>
    Task<IReadOnlyList<Tag>> GetBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken);

    /// <summary>Fügt einen neuen Tag hinzu.</summary>
    Task AddAsync(Tag tag, CancellationToken cancellationToken);

    /// <summary>Ersetzt sämtliche Tag-Verknüpfungen einer Markdown-Datei durch die übergebene Liste.</summary>
    Task ReplaceFileTagsAsync(Guid markdownFileId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken);

    /// <summary>
    /// Entfernt Schlagworte, an denen keine Datei mehr hängt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ein Schlagwort ist abgeleitet: Es entsteht, weil eine Datei es nennt. Nennt es keine
    /// mehr, ist die Zeile Datenmüll. Sichtbar wird sie nicht — die Auswertung für die Wolke
    /// verbindet über die Zuordnungen und lässt sie weg —, aber sie bleibt für immer stehen.
    /// </para>
    /// <para>
    /// Sichtbar wurde das am 16.08.2026 an 45 Farbwerten, die als Schlagwort in den Index
    /// geraten waren. Die Regel dagegen stand längst im Code; die Zeilen aus der Zeit davor
    /// hätte niemand mehr weggeräumt.
    /// </para>
    /// </remarks>
    /// <returns>Die Anzahl entfernter Schlagworte.</returns>
    Task<int> RemoveOrphanedTagsAsync(CancellationToken cancellationToken);

    /// <summary>Persistiert die ausstehenden Änderungen.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
