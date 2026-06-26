using MdExplorer.Core.Models;

namespace MdExplorer.Core.Abstractions;

/// <summary>
/// Persistenz für <see cref="MarkdownDocument"/>. Implementierung liegt in der Data-Schicht.
/// </summary>
public interface IMarkdownDocumentRepository
{
    /// <summary>Lädt das Dokument für die angegebene MarkdownFile-Id, sofern vorhanden.</summary>
    Task<MarkdownDocument?> GetByMarkdownFileIdAsync(Guid markdownFileId, CancellationToken cancellationToken);

    /// <summary>Liefert alle <c>MarkdownFile.Id</c>s, deren <see cref="MarkdownDocument.SourceContentHash"/> vom übergebenen Hash abweicht oder die kein Dokument haben.</summary>
    /// <param name="hashesByFileId">Aktueller Soll-Hash je <c>MarkdownFile.Id</c>.</param>
    /// <param name="cancellationToken">Abbruchsteuerung.</param>
    Task<IReadOnlyList<Guid>> GetStaleOrMissingAsync(IReadOnlyDictionary<Guid, string> hashesByFileId, CancellationToken cancellationToken);

    /// <summary>Fügt ein neues Dokument hinzu.</summary>
    Task AddAsync(MarkdownDocument document, CancellationToken cancellationToken);

    /// <summary>Aktualisiert ein bestehendes Dokument.</summary>
    void Update(MarkdownDocument document);

    /// <summary>Persistiert die ausstehenden Änderungen.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
