using MdExplorer.Core.Models;

namespace MdExplorer.Core.Abstractions;

/// <summary>
/// Persistenz für <see cref="ParseFailure"/>. Implementierung liegt in der Data-Schicht.
/// </summary>
public interface IParseFailureRepository
{
    /// <summary>
    /// Lädt die vorhandenen Fehlschlag-Vermerke zu den angegebenen MarkdownFile-Ids in einem
    /// gechunkten Batch. Fehlende Ids fehlen im Ergebnis.
    /// </summary>
    /// <param name="markdownFileIds">Gesuchte <c>MarkdownFile.Id</c>s.</param>
    /// <param name="cancellationToken">Abbruchsteuerung.</param>
    Task<IReadOnlyDictionary<Guid, ParseFailure>> GetByMarkdownFileIdsAsync(
        IReadOnlyCollection<Guid> markdownFileIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Legt den Vermerk für die Datei an oder überschreibt den vorhandenen. Höchstens ein
    /// Vermerk je Datei — ein zweiter Fehlschlag ersetzt den ersten.
    /// </summary>
    /// <param name="failure">Vermerk mit aktuellem Hash, Parser-Fassung und Grund.</param>
    /// <param name="cancellationToken">Abbruchsteuerung.</param>
    Task RecordAsync(ParseFailure failure, CancellationToken cancellationToken);

    /// <summary>Entfernt die Vermerke der angegebenen Dateien — aufzurufen, sobald eine Datei wieder parsbar ist.</summary>
    /// <param name="markdownFileIds">Betroffene <c>MarkdownFile.Id</c>s.</param>
    /// <param name="cancellationToken">Abbruchsteuerung.</param>
    Task RemoveAsync(IReadOnlyCollection<Guid> markdownFileIds, CancellationToken cancellationToken);

    /// <summary>Zählt die aktuell nicht verarbeitbaren Dateien — Grundlage für den Betriebs-Status.</summary>
    /// <param name="cancellationToken">Abbruchsteuerung.</param>
    Task<int> CountAsync(CancellationToken cancellationToken);

    /// <summary>Persistiert die ausstehenden Änderungen.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
