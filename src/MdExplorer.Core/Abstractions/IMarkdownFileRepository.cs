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

    /// <summary>Zählt alle Dateien im Index (Statusleiste in der UI).</summary>
    Task<int> CountAsync(CancellationToken cancellationToken);

    /// <summary>Fügt eine neue Datei hinzu.</summary>
    Task AddAsync(MarkdownFile entity, CancellationToken cancellationToken);

    /// <summary>Aktualisiert eine bestehende Datei.</summary>
    void Update(MarkdownFile entity);

    /// <summary>Entfernt eine Datei aus dem Index.</summary>
    void Remove(MarkdownFile entity);

    /// <summary>
    /// Entfernt viele Dateien auf einmal, unmittelbar und ohne <see cref="SaveChangesAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Der Aufräumdurchgang entfernt keine drei Einträge, sondern Zehntausende. Über
    /// <see cref="Remove"/> hieße das: jede Entität einzeln anhängen, vormerken und dabei
    /// jedes Mal den gesamten bereits vorgemerkten Bestand abgleichen lassen. Bei 27.000
    /// verwaisten Einträgen bleibt ein Lauf so bei voller Rechenlast stehen, ohne eine einzige
    /// Zeile zu schreiben.
    /// </para>
    /// <para>
    /// Deshalb geht die Löschung hier als Anweisung an die Datenbank, portionsweise und ohne
    /// Änderungsverfolgung. Dokumente, Schlagwort-Zuordnungen und Fehlschlag-Vermerke fallen über
    /// den Cascade-Delete der Datenbank mit weg. Die Zeilen im Volltext-Index räumt der
    /// Abgleich des Suchindex bei seinem nächsten Durchlauf ab. Weil jede Portion für sich
    /// abgeschlossen wird, ist ein abgebrochener Durchgang kein Schaden: Der nächste Abgleich
    /// holt den Rest.
    /// </para>
    /// <para>
    /// Was der Kontext zu den entfernten Dateien noch verfolgt, ist danach gelöst; andere
    /// vorgemerkte Änderungen im selben Kontext bleiben stehen.
    /// </para>
    /// </remarks>
    /// <param name="ids">Die Schlüssel der zu entfernenden Dateien.</param>
    /// <param name="cancellationToken">Abbruch-Merker.</param>
    /// <returns>Die Anzahl entfernter Einträge.</returns>
    Task<int> RemoveRangeAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Persistiert die ausstehenden Änderungen und liefert die Anzahl der Schreibzugriffe.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
