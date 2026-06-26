using System.ComponentModel.DataAnnotations;

namespace MdExplorer.TagCloud.Options;

/// <summary>
/// Konfiguration des Tag-Cloud-Moduls. Wird in <c>AppHostBuilder</c> über
/// <c>ValidateDataAnnotations</c> + <c>ValidateOnStart</c> geprüft.
/// </summary>
public sealed class TagCloudOptions
{
    /// <summary>Konfigurations-Sektion in <c>IConfiguration</c>.</summary>
    public const string SectionName = "TagCloud";

    /// <summary>Anzahl Top-N-Tags in der Cloud (Default 50, Long-Tail wird via UI-Toggle nachgeladen).</summary>
    [Range(1, 10_000)]
    public int TopN { get; set; } = 50;

    /// <summary>Erweiterte Top-N-Anzahl beim Long-Tail-Toggle (Default 1.000).</summary>
    [Range(1, 100_000)]
    public int LongTailTopN { get; set; } = 1_000;

    /// <summary>Minimale Schriftgröße der Tag-Cloud in DIP (Default 10).</summary>
    [Range(4.0, 96.0)]
    public double MinFontSize { get; set; } = 10.0;

    /// <summary>Maximale Schriftgröße der Tag-Cloud in DIP (Default 26).</summary>
    [Range(4.0, 96.0)]
    public double MaxFontSize { get; set; } = 26.0;

    /// <summary>
    /// Polling-Intervall des Hintergrund-Refresh in Sekunden (Default 5).
    /// Setzt einen Lower-Bound, damit Live-Updates höchstens diese Verzögerung haben.
    /// </summary>
    [Range(1, 600)]
    public int RefreshIntervalSeconds { get; set; } = 5;
}
