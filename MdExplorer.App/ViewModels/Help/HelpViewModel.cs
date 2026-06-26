using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MdExplorer.App.Services.Help;

namespace MdExplorer.App.ViewModels.Help;

/// <summary>
/// ViewModel des Hilfefensters: hält das Inhaltsverzeichnis und den aktuell
/// ausgewählten Eintrag. Die HTML-Anzeige übernimmt das WebView2-Element
/// im Code-Behind, weil sie nicht per Binding aktualisiert wird.
/// </summary>
internal sealed partial class HelpViewModel : ObservableObject
{
    /// <summary>Beobachtbares Inhaltsverzeichnis für die linke ListBox.</summary>
    public ObservableCollection<HelpTocEntry> Toc { get; } = [];

    [ObservableProperty]
    private HelpTocEntry? _selectedEntry;

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
}
