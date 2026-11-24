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

/// <summary>Was die Datei-Liste gerade zeigt.</summary>
/// <remarks>
/// Ein Zustand statt mehrerer bools nebeneinander: Zwei Merker, die sich widersprechen,
/// ergeben eine Ansicht, die es laut Code nicht geben kann, und genau die sieht dann der
/// Nutzer. Die Anzeige-Eigenschaften leiten sich hieraus ab.
/// </remarks>
internal enum AllFilesListState
{
    /// <summary>Der Bestand wird gerade geholt.</summary>
    Loading = 0,

    /// <summary>Es ist überhaupt nichts indiziert.</summary>
    EmptyStock = 1,

    /// <summary>Es gibt Einträge, aber keiner passt zu Suche und Filtern.</summary>
    NoMatches = 2,

    /// <summary>Es gibt etwas zu zeigen.</summary>
    Items = 3,

    /// <summary>Der Bestand konnte nicht geholt werden.</summary>
    LoadFailed = 4,
}
