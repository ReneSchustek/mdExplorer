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

/// <summary>Was der Zusammenhangs-Bereich gerade zeigt.</summary>
internal enum DocumentRelationsState
{
    /// <summary>Es ist kein Dokument geöffnet.</summary>
    NoDocument = 0,

    /// <summary>Die Verbindungen werden ermittelt.</summary>
    Loading = 1,

    /// <summary>Das Dokument steht für sich: keine Verweise, keine Kennzeichnungen, kein Unterordner.</summary>
    NothingRelated = 2,

    /// <summary>Es gibt Verbindungen zu zeigen.</summary>
    Relations = 3,

    /// <summary>Die Verbindungen ließen sich nicht ermitteln.</summary>
    Failed = 4,
}
