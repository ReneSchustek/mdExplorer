namespace MdExplorer.Indexer.Abstractions;

/// <summary>
/// Was die Oberfläche vom Markdown-Indexer sieht: den Erst-Scan auf Anforderung und dessen Fortschritt.
/// Der konkrete Indexer läuft als <c>BackgroundService</c> und wird über das DI-Hosting gestartet.
/// </summary>
public interface IIndexer
{
    /// <summary>Führt einen einmaligen, vollständigen Scan aller konfigurierten Wurzeln aus.</summary>
    /// <param name="cancellationToken">Abbruchsteuerung.</param>
    Task RunInitialScanAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Meldet geschriebene Stapel und das Ende eines Durchgangs je Wurzel. Der Erst-Scan meldet
    /// immer, damit sich die Oberfläche nach und nach füllt; ein späterer Abgleich nur, wenn er
    /// etwas geändert hat. Sonst lüde die Liste „Alle Dateien“ bei jedem Takt neu.
    /// </summary>
    event EventHandler<IndexerScanProgressEventArgs>? InitialScanProgress;
}

/// <summary>Fortschritts-Snapshot des Initial-Scans für eine Wurzel.</summary>
/// <param name="root">Wurzelpfad, auf den sich der Snapshot bezieht.</param>
/// <param name="processedCount">Anzahl der bisher in der DB persistierten Dateien für diese Wurzel.</param>
/// <param name="isCompleted">Wahr, wenn der Scan für diese Wurzel vollständig durch ist.</param>
public sealed class IndexerScanProgressEventArgs(string root, int processedCount, bool isCompleted) : EventArgs
{
    /// <summary>Wurzelpfad, auf den sich der Snapshot bezieht.</summary>
    public string Root { get; } = root ?? throw new ArgumentNullException(nameof(root));

    /// <summary>Anzahl der bisher in der DB persistierten Dateien für diese Wurzel.</summary>
    public int ProcessedCount { get; } = processedCount;

    /// <summary>Wahr, wenn der Scan für diese Wurzel vollständig durch ist.</summary>
    public bool IsCompleted { get; } = isCompleted;
}
