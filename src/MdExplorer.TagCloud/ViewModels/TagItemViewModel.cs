using CommunityToolkit.Mvvm.ComponentModel;
using MdExplorer.TagCloud.Models;

namespace MdExplorer.TagCloud.ViewModels;

/// <summary>
/// Darstellungs-Adapter für einen einzelnen Tag in der Cloud. Unveränderlich nach der Konstruktion.
/// </summary>
/// <remarks>
/// Die Schriftgröße rechnet die Wolke einmal je Snapshot aus. Je Schlagwort eine Mehrfachbindung
/// mit vier Vorfahren-Suchen kostete bei tausend Schlagworten bei jedem Neuaufbau spürbar.
/// </remarks>
public sealed partial class TagItemViewModel : ObservableObject
{
    /// <summary>Erstellt das ViewModel aus einer Statistik-Zeile.</summary>
    public TagItemViewModel(TagStatistic statistic, double fontSize)
    {
        ArgumentNullException.ThrowIfNull(statistic);
        FontSize = fontSize;
        Name = statistic.Name;
        Slug = statistic.Slug;
        Count = statistic.Count;
        LastUsedUtc = statistic.LastUsedUtc;
    }

    /// <summary>Schriftgröße in DIP, aus der Häufigkeit im Verhältnis zur ganzen Wolke.</summary>
    public double FontSize { get; }

    /// <summary>Original-Tag-Name (Anzeige).</summary>
    public string Name { get; }

    /// <summary>Normalisierter Slug, für Filter und Stable-Sort.</summary>
    public string Slug { get; }

    /// <summary>Häufigkeit über alle Dateien.</summary>
    public int Count { get; }

    /// <summary>Zuletzt verwendet (MAX <c>MarkdownFile.LastWriteTimeUtc</c>) in UTC.</summary>
    public DateTime LastUsedUtc { get; }
}
