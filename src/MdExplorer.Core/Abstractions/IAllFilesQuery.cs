namespace MdExplorer.Core.Abstractions;

/// <summary>
/// Liefert einen flachen Snapshot über alle indizierten Markdown-Dateien inkl. ihrer Tags.
/// Implementierung liegt in der Data-Schicht (EF-Core), damit die UI EF-frei bleibt,
/// als analoges Pattern zu <see cref="ITagStatisticsQuery"/> und <see cref="IGraphSourceProvider"/>.
/// </summary>
public interface IAllFilesQuery
{
    /// <summary>
    /// Lädt alle Markdown-Dateien mit ihren Tag-Slugs in zwei Abfragen, eine für die Dateien
    /// und eine für alle Zuordnungen; eine Abfrage je Datei gibt es nicht.
    /// Reihenfolge: <see cref="AllFilesRow.LastWriteTimeUtc"/> absteigend, bei Gleichstand
    /// <see cref="AllFilesRow.RelativePath"/> aufsteigend (stabil für UI-Bindings).
    /// </summary>
    Task<IReadOnlyList<AllFilesRow>> GetAllAsync(CancellationToken cancellationToken);
}

/// <summary>Aggregat-Zeile für die flache Datei-Liste.</summary>
/// <param name="MarkdownFileId">Stabiler Schlüssel, der Brückenwert zu den anderen Diensten.</param>
/// <param name="Title">Dateiname ohne Erweiterung.</param>
/// <param name="RelativePath">Pfad relativ zum konfigurierten Root.</param>
/// <param name="AbsolutePath">Vollqualifizierter Pfad (für Navigation).</param>
/// <param name="LastWriteTimeUtc">Letzte Änderung auf Disk (UTC).</param>
/// <param name="TagSlugs">Slugs der auf diese Datei angewendeten Tags, alphabetisch nach Slug. Doppelte gibt es nicht, weil Datei und Tag zusammen den Schlüssel der Zuordnung bilden.</param>
public sealed record AllFilesRow(
    Guid MarkdownFileId,
    string Title,
    string RelativePath,
    string AbsolutePath,
    DateTime LastWriteTimeUtc,
    IReadOnlyList<string> TagSlugs);
