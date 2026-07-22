namespace MdExplorer.Core.Abstractions;

/// <summary>
/// Liefert einen flachen Snapshot ueber alle indizierten Markdown-Dateien inkl. ihrer Tags.
/// Implementierung liegt in der Data-Schicht (EF-Core), damit die UI EF-frei bleibt —
/// analoges Pattern zu <see cref="ITagStatisticsQuery"/> und <see cref="IGraphSourceProvider"/>.
/// </summary>
public interface IAllFilesQuery
{
    /// <summary>
    /// Laedt alle Markdown-Dateien mit ihren Tag-Slugs in einem Query-Roundtrip.
    /// Reihenfolge: <see cref="AllFilesRow.LastWriteTimeUtc"/> absteigend, bei Gleichstand
    /// <see cref="AllFilesRow.RelativePath"/> aufsteigend (stabil fuer UI-Bindings).
    /// </summary>
    Task<IReadOnlyList<AllFilesRow>> GetAllAsync(CancellationToken cancellationToken);
}

/// <summary>Aggregat-Zeile fuer die flache Datei-Liste.</summary>
/// <param name="MarkdownFileId">Stabiler Schluessel — Brueckenwert zu den anderen Diensten.</param>
/// <param name="Title">Dateiname ohne Erweiterung.</param>
/// <param name="RelativePath">Pfad relativ zum konfigurierten Root.</param>
/// <param name="AbsolutePath">Vollqualifizierter Pfad (fuer Navigation).</param>
/// <param name="LastWriteTimeUtc">Letzte Aenderung auf Disk (UTC).</param>
/// <param name="TagSlugs">Slugs der auf diese Datei angewendeten Tags (deduziert in Insertion-Order).</param>
public sealed record AllFilesRow(
    Guid MarkdownFileId,
    string Title,
    string RelativePath,
    string AbsolutePath,
    DateTime LastWriteTimeUtc,
    IReadOnlyList<string> TagSlugs);
