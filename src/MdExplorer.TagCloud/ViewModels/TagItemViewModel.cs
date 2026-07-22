using CommunityToolkit.Mvvm.ComponentModel;
using MdExplorer.TagCloud.Models;

namespace MdExplorer.TagCloud.ViewModels;

/// <summary>
/// Darstellungs-Adapter für einen einzelnen Tag in der Cloud. Immutable nach Konstruktion —
/// die Größe wird vom Konverter im XAML berechnet, sodass das Item nur Roh-Daten trägt.
/// </summary>
public sealed partial class TagItemViewModel : ObservableObject
{
    /// <summary>Erstellt das ViewModel aus einer Statistik-Zeile.</summary>
    public TagItemViewModel(TagStatistic statistic)
    {
        ArgumentNullException.ThrowIfNull(statistic);
        Name = statistic.Name;
        Slug = statistic.Slug;
        Count = statistic.Count;
        LastUsedUtc = statistic.LastUsedUtc;
    }

    /// <summary>Original-Tag-Name (Anzeige).</summary>
    public string Name { get; }

    /// <summary>Normalisierter Slug — für Filter und Stable-Sort.</summary>
    public string Slug { get; }

    /// <summary>Häufigkeit über alle Dateien.</summary>
    public int Count { get; }

    /// <summary>Zuletzt verwendet (MAX <c>MarkdownFile.LastWriteTimeUtc</c>) in UTC.</summary>
    public DateTime LastUsedUtc { get; }
}
