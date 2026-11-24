using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Localization;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Diagnostics;
using MdExplorer.Core.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>Item-View für einen Datei-Eintrag im Alle-Dateien-Tab.</summary>
internal sealed class AllFilesItemViewModel
{
    /// <summary>Erzeugt einen Eintrag aus einer Query-Zeile.</summary>
    public AllFilesItemViewModel(AllFilesRow row, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(timeZone);
        MarkdownFileId = row.MarkdownFileId;
        Title = row.Title;
        RelativePath = row.RelativePath;
        AbsolutePath = row.AbsolutePath;
        LastWriteTimeUtc = row.LastWriteTimeUtc;
        LastWriteTime = TimeZoneInfo.ConvertTimeFromUtc(row.LastWriteTimeUtc, timeZone);
        TagSlugs = row.TagSlugs;
        FolderPath = RelativeFolder.Of(row.RelativePath);
    }

    /// <summary>Stabiler Schlüssel.</summary>
    public Guid MarkdownFileId { get; }

    /// <summary>Dateiname ohne Erweiterung.</summary>
    public string Title { get; }

    /// <summary>Pfad relativ zum konfigurierten Root.</summary>
    public string RelativePath { get; }

    /// <summary>Vollqualifizierter Pfad, Eingabe für den Navigations-Locator.</summary>
    public string AbsolutePath { get; }

    /// <summary>Letzte Änderung auf Disk (UTC), Grundlage für Sortierung und Zeitraumfilter.</summary>
    public DateTime LastWriteTimeUtc { get; }

    /// <summary>
    /// Dieselbe Änderung in der Zeitzone des Rechners. Nur diese wird angezeigt: Wer eine
    /// Datei gerade bearbeitet hat, erwartet die Uhrzeit seiner eigenen Uhr.
    /// </summary>
    public DateTime LastWriteTime { get; }

    /// <summary>Slugs der angewendeten Tags.</summary>
    public IReadOnlyList<string> TagSlugs { get; }

    /// <summary>
    /// Beschriftung der Gruppe, in der dieser Eintrag steht; leer, wenn nicht gruppiert wird.
    /// </summary>
    public string GroupLabel { get; internal set; } = string.Empty;

    /// <summary>Der Ordner relativ zur Wurzel; leer für Dateien, die direkt darin liegen.</summary>
    public string FolderPath { get; }
}
