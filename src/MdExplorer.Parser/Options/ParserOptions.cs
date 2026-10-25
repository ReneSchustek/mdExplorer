using System.ComponentModel.DataAnnotations;

namespace MdExplorer.Parser.Options;

/// <summary>
/// Konfiguration des Parser-Moduls. Wird beim Hosten via <c>ValidateDataAnnotations</c> + <c>ValidateOnStart</c> geprüft.
/// </summary>
public sealed class ParserOptions
{
    /// <summary>Konfigurations-Sektion in <c>IConfiguration</c>.</summary>
    public const string SectionName = "Parser";

    // Die Obergrenzen fangen offensichtliche Fehleinträge ab; die Werte selbst sind gesetzt, nicht gemessen.

    /// <summary>Obergrenze für <see cref="MaxParallelism"/>.</summary>
    private const int MaxParallelismUpperBound = 128;

    /// <summary>Obergrenze (in Sekunden) für <see cref="PollIntervalSeconds"/>.</summary>
    private const int PollIntervalSecondsUpperBound = 600;

    /// <summary>Obergrenze für <see cref="BatchSize"/>.</summary>
    private const int BatchSizeUpperBound = 10_000;

    /// <summary>Untergrenze für <see cref="MaxFileSizeBytes"/>.</summary>
    private const long MaxFileSizeBytesLowerBound = 64 * 1024;

    /// <summary>Obergrenze für <see cref="MaxFileSizeBytes"/>.</summary>
    private const long MaxFileSizeBytesUpperBound = 256L * 1024 * 1024;

    /// <summary>
    /// Maximale Anzahl paralleler Parse-Vorgänge, jeder mit Lesen der Datei und Parsen.
    /// Default: ein Vorgang je Kern, höchstens so viele wie die Obergrenze zulässt. Auf einer
    /// Maschine mit mehr Kernen stünde die Voreinstellung sonst außerhalb der eigenen Prüfung, und
    /// der Start schlüge fehl.
    /// </summary>
    [Range(1, MaxParallelismUpperBound)]
    public int MaxParallelism { get; set; } = Math.Min(Environment.ProcessorCount, MaxParallelismUpperBound);

    /// <summary>Voreinstellung für <see cref="MaxFileSizeBytes"/>: 4 MiB, Begründung dort.</summary>
    private const long DefaultMaxFileSizeBytes = 4L * 1024 * 1024;

    /// <summary>
    /// Größte Datei in Byte, die der Parser liest. Default: 4 MiB.
    /// </summary>
    /// <remarks>
    /// Im Messbestand ist die größte Notiz 182 KB groß und braucht 4 ms; 4 MiB lassen gut
    /// zwanzigfachen Spielraum. Was darüber liegt, ist fast immer ein Protokoll oder ein Export mit
    /// der Endung .md. Ungebremst kostete es je parallelem Vorgang ein Vielfaches seiner Größe an
    /// Speicher (Bytes, Text, Syntaxbaum, HTML). Eine zu große Datei bekommt einen Vermerk und wird
    /// erst wieder versucht, wenn sich ihr Inhalt ändert.
    /// </remarks>
    [Range(MaxFileSizeBytesLowerBound, MaxFileSizeBytesUpperBound)]
    public long MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;

    /// <summary>Pollingintervall (in Sekunden), in dem der Orchestrator nach ungeparsten Dateien sucht. Default: 5 s.</summary>
    [Range(1, PollIntervalSecondsUpperBound)]
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Maximale Anzahl Dokumente pro Orchestrator-Schritt. Default: 100.</summary>
    [Range(1, BatchSizeUpperBound)]
    public int BatchSize { get; set; } = 100;
}
