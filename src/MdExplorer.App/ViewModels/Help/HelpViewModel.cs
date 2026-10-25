using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MdExplorer.App.Services.Help;

namespace MdExplorer.App.ViewModels.Help;

/// <summary>
/// ViewModel des Hilfefensters: hält das Inhaltsverzeichnis, den ausgewählten Eintrag und den
/// Stand der Suche. Die HTML-Anzeige übernimmt das WebView2-Element im Code-Behind, weil sie
/// nicht per Binding aktualisiert wird.
/// </summary>
internal sealed partial class HelpViewModel : ObservableObject
{
    /// <summary>Beobachtbares Inhaltsverzeichnis für die linke ListBox.</summary>
    public ObservableCollection<HelpTocEntry> Toc { get; } = [];

    [ObservableProperty]
    private HelpTocEntry? _selectedEntry;

    /// <summary>Stand der Suche im Handbuch, etwa „2 / 7“.</summary>
    [ObservableProperty]
    private string _hitSummary = FormatHits(0, -1);

    /// <summary>Schreibt eine neue TOC-Liste in die <see cref="Toc"/>-Collection.</summary>
    public void SetToc(IReadOnlyList<HelpTocEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Toc.Clear();
        foreach (HelpTocEntry entry in entries)
        {
            Toc.Add(entry);
        }
    }

    /// <summary>Der Eintrag zum Anker <paramref name="slug"/>, oder <see langword="null"/>.</summary>
    /// <param name="slug">Anker einer Überschrift.</param>
    public HelpTocEntry? FindEntry(string slug)
    {
        ArgumentNullException.ThrowIfNull(slug);

        return Toc.FirstOrDefault(entry => string.Equals(entry.Slug, slug, StringComparison.Ordinal));
    }

    /// <summary>Meldet, was die Suche im Handbuch gefunden hat.</summary>
    /// <param name="count">Anzahl der Treffer.</param>
    /// <param name="index">Treffer, auf dem die Markierung steht, ab 0; -1 für keinen.</param>
    public void ShowHits(int count, int index) => HitSummary = FormatHits(count, index);

    // Gezählt wird für Menschen ab eins; ohne Treffer steht „0 / 0“.
    private static string FormatHits(int count, int index) =>
        count <= 0
            ? "0 / 0"
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(index, 0) + 1} / {count}");
}
