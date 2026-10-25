using System.ComponentModel.DataAnnotations;

namespace MdExplorer.Indexer.Options;

/// <summary>
/// Konfiguration des Markdown-Indexers für Betriebs-Parameter (Debounce, Batching,
/// Symlinks). Index-Roots, Ausschluss-Muster und das Resync-Intervall liegen in der
/// <c>AppSettings</c>-Datei und werden über
/// <see cref="MdExplorer.Core.Abstractions.ISettingsService"/> bezogen.
/// </summary>
public sealed class IndexerOptions
{
    /// <summary>Konfigurations-Sektion in <c>IConfiguration</c>.</summary>
    public const string SectionName = "Indexer";

    // Die Grenzen prüft der Host beim Start; ein Wert außerhalb hält die Anwendung an, statt sie
    // mit einem unsinnigen Wert laufen zu lassen. Die Grenzwerte selbst sind gesetzt, nicht gemessen.

    /// <summary>Untere Grenze der zulässigen Debounce-Wartezeit in Millisekunden.</summary>
    private const int MinDebounceMs = 50;

    /// <summary>Obere Grenze der zulässigen Debounce-Wartezeit in Millisekunden.</summary>
    private const int MaxDebounceMs = 5_000;

    /// <summary>Obere Grenze der zulässigen Batch-Größe.</summary>
    private const int MaxBatchSize = 1_000;

    /// <summary>Obere Grenze der zulässigen Initial-Scan-Batch-Größe.</summary>
    private const int MaxInitialScanBatchSize = 5_000;

    /// <summary>
    /// Wartezeit pro Pfad, bevor ein Watcher-Ereignis als stabil gilt (Debounce). Ein Editor meldet
    /// beim Speichern oft mehrere Ereignisse kurz hintereinander; 300 ms fassen sie zu einem zusammen.
    /// </summary>
    [Range(MinDebounceMs, MaxDebounceMs)]
    public int DebounceMs { get; set; } = 300;

    /// <summary>Maximale Anzahl der Ereignisse, die in einem Batch in die Datenbank geschrieben werden.</summary>
    [Range(1, MaxBatchSize)]
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Abgleich einer Wurzel: nach wie vielen geänderten Dateien ein Zwischen-Commit auf die
    /// SQLite-DB erfolgt. Verhindert, dass UI-Komponenten („Alle Dateien"-Tab, Folder-Tree)
    /// minutenlang leer bleiben, wenn die Wurzel mehrere Tausend Markdown-Dateien enthält.
    /// </summary>
    [Range(1, MaxInitialScanBatchSize)]
    public int InitialScanBatchSize { get; set; } = 100;

    /// <summary>
    /// Steuert, ob der Indexer Symlinks und NTFS-Junctions (Reparse-Points) verfolgt.
    /// Default <c>false</c>: Verknüpfte Ordner und Dateien werden übersprungen, weil sie in
    /// gewachsenen Arbeitsordnern Endlosschleifen, doppelte Indizierung und Inhalte von außerhalb
    /// der Wurzel bringen können. Eine Datei, die als Reparse-Point an Ort und Stelle liegt (etwa
    /// ein OneDrive-Platzhalter), ist keine Verknüpfung und wird indiziert.
    /// Ist der Schalter an, erkennt die Breitensuche Zyklen über den kanonischen Endpfad.
    /// </summary>
    public bool FollowSymlinks { get; set; }
}
