using System.ComponentModel.DataAnnotations;

namespace MdExplorer.Parser.Options;

/// <summary>
/// Konfiguration des Parser-Moduls. Wird beim Hosten via <c>ValidateDataAnnotations</c> + <c>ValidateOnStart</c> geprüft.
/// </summary>
public sealed class ParserOptions
{
    /// <summary>Konfigurations-Sektion in <c>IConfiguration</c>.</summary>
    public const string SectionName = "Parser";

    /// <summary>Obergrenze für <see cref="MaxParallelism"/>.</summary>
    private const int MaxParallelismUpperBound = 128;

    /// <summary>Obergrenze (in Sekunden) für <see cref="PollIntervalSeconds"/>.</summary>
    private const int PollIntervalSecondsUpperBound = 600;

    /// <summary>Obergrenze für <see cref="BatchSize"/>.</summary>
    private const int BatchSizeUpperBound = 10_000;

    /// <summary>Maximale Anzahl paralleler Parse-Vorgänge. Default: <see cref="Environment.ProcessorCount"/>.</summary>
    [Range(1, MaxParallelismUpperBound)]
    public int MaxParallelism { get; set; } = Environment.ProcessorCount;

    /// <summary>Pollingintervall (in Sekunden), in dem der Orchestrator nach ungeparsten Dateien sucht. Default: 5 s.</summary>
    [Range(1, PollIntervalSecondsUpperBound)]
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Maximale Anzahl Dokumente pro Orchestrator-Schritt. Default: 100.</summary>
    [Range(1, BatchSizeUpperBound)]
    public int BatchSize { get; set; } = 100;
}
