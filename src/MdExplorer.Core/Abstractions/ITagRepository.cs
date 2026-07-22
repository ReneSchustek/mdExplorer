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

    /// <summary>Persistiert die ausstehenden Änderungen.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
