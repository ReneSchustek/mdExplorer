using MdExplorer.Core.Abstractions;
using MdExplorer.Search.Models;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Einzelner Eintrag in der Trefferliste. Kapselt das <see cref="SearchResult"/> des
/// Search-Moduls und exponiert die im Binding benötigten Eigenschaften.
/// </summary>
internal sealed class SearchResultItemViewModel
{
    /// <summary>Erzeugt einen Eintrag aus einem <see cref="SearchResult"/>.</summary>
    public SearchResultItemViewModel(SearchResult source)
    {
        ArgumentNullException.ThrowIfNull(source);
        MarkdownFileId = source.MarkdownFileId;
        Title = source.Title;
        Path = source.Path;
        Snippet = source.Snippet;
    }

    /// <summary>Schlüssel der Markdown-Datei.</summary>
    public Guid MarkdownFileId { get; }

    /// <summary>Anzeige-Titel des Treffers.</summary>
    public string Title { get; }

    /// <summary>Relativer Pfad zur Anzeige unter dem Titel.</summary>
    public string Path { get; }

    /// <summary>Textausschnitt; die Trefferstellen stehen zwischen den <see cref="SnippetMarkers"/>.</summary>
    public string Snippet { get; }
}
