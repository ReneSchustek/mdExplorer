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

    /// <summary>Obergrenze der validierten Top-N-Anzahl.</summary>
    private const int MaxTopN = 10_000;

    /// <summary>Obergrenze der validierten Long-Tail-Top-N-Anzahl.</summary>
    private const int MaxLongTailTopN = 100_000;

    /// <summary>Untere Schranke der validierten Schriftgröße in DIP.</summary>
    private const double MinAllowedFontSize = 4.0;

    /// <summary>Obere Schranke der validierten Schriftgröße in DIP.</summary>
    private const double MaxAllowedFontSize = 96.0;

    /// <summary>Obergrenze des validierten Refresh-Intervalls in Sekunden.</summary>
    private const int MaxRefreshIntervalSeconds = 600;

    /// <summary>Anzahl Top-N-Tags in der Cloud (Default 50, Long-Tail wird via UI-Toggle nachgeladen).</summary>
    [Range(1, MaxTopN)]
    public int TopN { get; set; } = 50;

    /// <summary>Erweiterte Top-N-Anzahl beim Long-Tail-Toggle (Default 1.000).</summary>
    [Range(1, MaxLongTailTopN)]
    public int LongTailTopN { get; set; } = 1_000;

    /// <summary>Minimale Schriftgröße der Tag-Cloud in DIP (Default 10).</summary>
    [Range(MinAllowedFontSize, MaxAllowedFontSize)]
    public double MinFontSize { get; set; } = 10.0;

    /// <summary>Maximale Schriftgröße der Tag-Cloud in DIP (Default 26).</summary>
    [Range(MinAllowedFontSize, MaxAllowedFontSize)]
    public double MaxFontSize { get; set; } = 26.0;

    /// <summary>
    /// Polling-Intervall des Hintergrund-Refresh in Sekunden (Default 5).
    /// Setzt einen Lower-Bound, damit Live-Updates höchstens diese Verzögerung haben.
    /// </summary>
    [Range(1, MaxRefreshIntervalSeconds)]
    public int RefreshIntervalSeconds { get; set; } = 5;
}
