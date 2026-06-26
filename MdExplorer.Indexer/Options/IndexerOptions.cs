using System.ComponentModel.DataAnnotations;

namespace MdExplorer.Indexer.Options;

/// <summary>
/// Konfiguration des Markdown-Indexers für Betriebs-Parameter (Debounce, Batching,
/// Resync-Intervall). Index-Roots und Ausschluss-Muster liegen in der
/// <c>AppSettings</c>-Datei und werden über
/// <see cref="MdExplorer.Core.Abstractions.ISettingsService"/> bezogen.
/// </summary>
public sealed class IndexerOptions
{
    /// <summary>Konfigurations-Sektion in <c>IConfiguration</c>.</summary>
    public const string SectionName = "Indexer";

    /// <summary>Wartezeit pro Pfad, bevor ein Watcher-Ereignis als stabil gilt (Debounce).</summary>
    [Range(50, 5_000)]
    public int DebounceMs { get; set; } = 300;

    /// <summary>Maximale Anzahl der Ereignisse, die in einem Batch in die Datenbank geschrieben werden.</summary>
    [Range(1, 1_000)]
    public int BatchSize { get; set; } = 50;

    /// <summary>Zeitfenster, nach dem ein unvollständiger Batch geleert wird (in Millisekunden).</summary>
    [Range(50, 60_000)]
    public int BatchFlushIntervalMs { get; set; } = 500;

    /// <summary>
    /// Initial-Scan: nach wie vielen aufgenommenen Dateien ein Zwischen-Commit auf die
    /// SQLite-DB erfolgt. Verhindert, dass UI-Komponenten („Alle Dateien"-Tab, Folder-Tree)
    /// minutenlang leer bleiben, wenn die Wurzel mehrere Tausend Markdown-Dateien enthält.
    /// </summary>
    [Range(1, 5_000)]
    public int InitialScanBatchSize { get; set; } = 100;

    /// <summary>
    /// Steuert, ob der Indexer Symlinks und NTFS-Junctions (Reparse-Points) verfolgt.
    /// Default <c>false</c> — Reparse-Points werden komplett übersprungen, weil sie in
    /// realen Workspaces wie <c>F:\Entwicklung</c> Endlosschleifen und doppelte
    /// Indizierung verursachen können. Power-User können den Schalter
    /// aktivieren; dann erkennt der BFS Zyklen über den kanonischen Endpfad.
    /// </summary>
    public bool FollowSymlinks { get; set; }
}
