using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Services;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Diagnostics;
using MdExplorer.Core.Models;
using MdExplorer.Graph.Abstractions;
using MdExplorer.Graph.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>Ein Dokument am anderen Ende einer Verbindung, wie es die Ansicht zeigt.</summary>
internal sealed class RelatedDocumentViewModel
{
    /// <summary>Erzeugt den Eintrag aus einem Ergebnis des Graphen.</summary>
    public RelatedDocumentViewModel(RelatedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        MarkdownFileId = document.MarkdownFileId;
        Title = document.Title;
        RelativePath = document.RelativePath;
    }

    /// <summary>Stabiler Schlüssel, Eingang für das Öffnen.</summary>
    public Guid MarkdownFileId { get; }

    /// <summary>Dateiname ohne Erweiterung.</summary>
    public string Title { get; }

    /// <summary>Pfad relativ zur Wurzel; unterscheidet gleichnamige Dateien.</summary>
    public string RelativePath { get; }
}
