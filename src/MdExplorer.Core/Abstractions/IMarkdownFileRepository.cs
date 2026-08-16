using MdExplorer.Core.Models;

namespace MdExplorer.Core.Abstractions;

/// <summary>
/// Persistenzabstraktion für indizierte Markdown-Dateien.
/// Implementierung liegt in der Data-Schicht, damit die Feature-Module EF-Core-frei bleiben.
/// </summary>
public interface IMarkdownFileRepository
{
    /// <summary>Lädt eine Datei anhand ihres kanonischen Pfads.</summary>
    Task<MarkdownFile?> GetByAbsolutePathAsync(string absolutePath, CancellationToken cancellationToken);

    /// <summary>Lädt eine Datei anhand ihrer Primärschlüssel-Id.</summary>
    Task<MarkdownFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Lädt alle Dateien unterhalb der angegebenen Wurzel.</summary>
    Task<IReadOnlyList<MarkdownFile>> GetAllUnderRootAsync(string rootAbsolutePath, CancellationToken cancellationToken);

    /// <summary>
    /// Sucht eine Datei anhand des Dateinamens ohne Erweiterung (Vergleich case-insensitive).
    /// Liefert die <c>Id</c> der ersten in stabiler Reihenfolge gefundenen Datei oder
    /// <see langword="null"/>, wenn keine existiert. Wird vom UI für die WikiLink-Auflösung verwendet.
    /// </summary>
    Task<Guid?> FindIdByFileNameAsync(string fileNameWithoutExtension, CancellationToken cancellationToken);

    /// <summary>Zählt alle Dateien im Index — Statusleiste in der UI.</summary>
    Task<int> CountAsync(CancellationToken cancellationToken);

    /// <summary>Fügt eine neue Datei hinzu.</summary>
    Task AddAsync(MarkdownFile entity, CancellationToken cancellationToken);

    /// <summary>Aktualisiert eine bestehende Datei.</summary>
    void Update(MarkdownFile entity);

    /// <summary>Entfernt eine Datei aus dem Index.</summary>
    void Remove(MarkdownFile entity);

    /// <summary>
    /// Entfernt viele Dateien auf einmal — unmittelbar, ohne <see cref="SaveChangesAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Der Aufräumdurchgang entfernt keine drei Einträge, sondern Zehntausende. Über
    /// <see cref="Remove"/> hieße das: jede Entität einzeln anhängen, vormerken und dabei
    /// jedes Mal den gesamten bereits vorgemerkten Bestand abgleichen lassen. Am 16.08.2026
    /// blieb ein Lauf über 27.000 verwaiste Einträge deshalb zweimal hintereinander bei voller
    /// Rechenlast stehen, ohne eine einzige Zeile zu schreiben.
    /// </para>
    /// <para>
    /// Diese Methode geht den anderen Weg: Sie setzt die Löschung als Anweisung ab, portionsweise
    /// und ohne Änderungsverfolgung. Was an den Einträgen hängt — Dokumente, Zuordnungen und der
    /// Volltext —, fällt über die Regeln der Datenbank mit weg; das ist dort hinterlegt und nicht
    /// hier. Weil jede Portion für sich abgeschlossen wird, ist ein abgebrochener Durchgang kein
    /// Schaden: Der nächste Abgleich holt den Rest.
    /// </para>
    /// </remarks>
    /// <param name="ids">Die Schlüssel der zu entfernenden Dateien.</param>
    /// <param name="cancellationToken">Abbruch-Merker.</param>
    /// <returns>Die Anzahl entfernter Einträge.</returns>
    Task<int> RemoveRangeAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Persistiert die ausstehenden Änderungen und liefert die Anzahl der Schreibzugriffe.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
