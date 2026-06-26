using System.ComponentModel.DataAnnotations;

namespace MdExplorer.Parser.Options;

/// <summary>
/// Konfiguration des Parser-Moduls. Wird beim Hosten via <c>ValidateDataAnnotations</c> + <c>ValidateOnStart</c> geprüft.
/// </summary>
public sealed class ParserOptions
{
    /// <summary>Konfigurations-Sektion in <c>IConfiguration</c>.</summary>
    public const string SectionName = "Parser";

    /// <summary>Maximale Anzahl paralleler Parse-Vorgänge. Default: <see cref="Environment.ProcessorCount"/>.</summary>
    [Range(1, 128)]
    public int MaxParallelism { get; set; } = Environment.ProcessorCount;

    /// <summary>Pollingintervall (in Sekunden), in dem der Orchestrator nach ungeparsten Dateien sucht. Default: 5 s.</summary>
    [Range(1, 600)]
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Maximale Anzahl Dokumente pro Orchestrator-Schritt. Default: 100.</summary>
    [Range(1, 10_000)]
    public int BatchSize { get; set; } = 100;
}
