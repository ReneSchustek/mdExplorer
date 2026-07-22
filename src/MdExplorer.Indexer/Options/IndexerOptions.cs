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

    /// <summary>Untere Grenze der zulässigen Debounce-Wartezeit in Millisekunden.</summary>
    private const int MinDebounceMs = 50;

    /// <summary>Obere Grenze der zulässigen Debounce-Wartezeit in Millisekunden.</summary>
    private const int MaxDebounceMs = 5_000;

    /// <summary>Obere Grenze der zulässigen Batch-Größe.</summary>
    private const int MaxBatchSize = 1_000;

    /// <summary>Untere Grenze des zulässigen Batch-Flush-Zeitfensters in Millisekunden.</summary>
    private const int MinBatchFlushIntervalMs = 50;

    /// <summary>Obere Grenze des zulässigen Batch-Flush-Zeitfensters in Millisekunden.</summary>
    private const int MaxBatchFlushIntervalMs = 60_000;

    /// <summary>Obere Grenze der zulässigen Initial-Scan-Batch-Größe.</summary>
    private const int MaxInitialScanBatchSize = 5_000;

    /// <summary>Wartezeit pro Pfad, bevor ein Watcher-Ereignis als stabil gilt (Debounce).</summary>
    [Range(MinDebounceMs, MaxDebounceMs)]
    public int DebounceMs { get; set; } = 300;

    /// <summary>Maximale Anzahl der Ereignisse, die in einem Batch in die Datenbank geschrieben werden.</summary>
    [Range(1, MaxBatchSize)]
    public int BatchSize { get; set; } = 50;

    /// <summary>Zeitfenster, nach dem ein unvollständiger Batch geleert wird (in Millisekunden).</summary>
    [Range(MinBatchFlushIntervalMs, MaxBatchFlushIntervalMs)]
    public int BatchFlushIntervalMs { get; set; } = 500;

    /// <summary>
    /// Initial-Scan: nach wie vielen aufgenommenen Dateien ein Zwischen-Commit auf die
    /// SQLite-DB erfolgt. Verhindert, dass UI-Komponenten („Alle Dateien"-Tab, Folder-Tree)
    /// minutenlang leer bleiben, wenn die Wurzel mehrere Tausend Markdown-Dateien enthält.
    /// </summary>
    [Range(1, MaxInitialScanBatchSize)]
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
