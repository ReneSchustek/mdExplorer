using System.Collections.Immutable;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.Extensions.FileSystemGlobbing;

namespace MdExplorer.Core.Settings;

/// <summary>Klassifizierung des betroffenen Settings-Feldes.</summary>
public enum SettingsField
{
    /// <summary>Roots-Liste (Indexer).</summary>
    Roots,

    /// <summary>Ausschluss-Muster-Liste (Indexer).</summary>
    ExclusionPatterns,

    /// <summary>Preview-Schriftgröße.</summary>
    PreviewFontSize,

    /// <summary>Trefferanzahl pro Seite.</summary>
    ResultsPerPage,

    /// <summary>Such-Debounce in Millisekunden.</summary>
    SearchDebounceMs,

    /// <summary>Indexer-Resync-Intervall in Sekunden.</summary>
    IndexerResyncIntervalSeconds,

    /// <summary>Per Kontextmenü pausierte Ordner (Indexer).</summary>
    UiExcludedFolders,

    /// <summary>Erscheinungsbild.</summary>
    Theme,
}
