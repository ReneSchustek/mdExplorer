using System.ComponentModel.DataAnnotations;

namespace MdExplorer.Core.Models;

/// <summary>
/// Wurzel-Datensatz für die Settings-Datei <c>settings.json</c> unterhalb
/// <c>%LOCALAPPDATA%\MdExplorer\</c>. Ist die Datei nicht vorhanden oder
/// das Schema veraltet, liefert <see cref="Default"/> einen vollständigen Default-Stand.
/// </summary>
/// <param name="SchemaVersion">Versionsnummer des persistierten Schemas — erlaubt Migrations-Pipeline.</param>
/// <param name="Indexing">Indexierungsbezogene Einstellungen (Roots + Excludes).</param>
/// <param name="Appearance">Darstellungsbezogene Einstellungen.</param>
/// <param name="Behavior">Verhaltensbezogene Einstellungen.</param>
public sealed record AppSettings(
    int SchemaVersion,
    IndexingSettings Indexing,
    AppearanceSettings Appearance,
    BehaviorSettings Behavior)
{
    /// <summary>Aktuell unterstützte Schema-Version.</summary>
    public const int CurrentSchemaVersion = 3;

    /// <summary>
    /// Liefert eine vollständig defaultierte Settings-Instanz. Wird verwendet,
    /// wenn keine Datei existiert oder das Laden fehlschlägt.
    /// </summary>
    public static AppSettings Default => new(
        CurrentSchemaVersion,
        IndexingSettings.Default,
        AppearanceSettings.Default,
        BehaviorSettings.Default);
}

/// <summary>Settings für Index-Roots und Ausschluss-Muster.</summary>
/// <param name="Roots">Wurzelpfade, die rekursiv nach <c>*.md</c> durchsucht werden.</param>
/// <param name="ExclusionPatterns">Glob-Muster für Ausschlüsse (mit <c>!</c>-Negation).</param>
/// <param name="UiExcludedFolders">Absolute Pfade einzelner Ordner, die der Nutzer
/// per Folder-Tree-Kontextmenü aus der Indexierung ausgeschlossen hat.
/// Wirkt additiv zu <paramref name="ExclusionPatterns"/> und <c>.mdignore</c>-Hierarchie.</param>
/// <param name="AutoExtractHashtags">Wenn <see langword="true"/>, extrahiert
/// der Indexer Hashtags aus dem Markdown-Body. Frontmatter-<c>tags</c>-Werte werden
/// unabhaengig davon stets uebernommen, weil sie explizit gesetzt sind.</param>
public sealed record IndexingSettings(
    IReadOnlyList<string> Roots,
    IReadOnlyList<string> ExclusionPatterns,
    IReadOnlyList<string> UiExcludedFolders,
    bool AutoExtractHashtags)
{
    /// <summary>Default-Ausschlussmuster — übernehmen die früheren hartverdrahteten Ordnernamen.</summary>
    public static readonly IReadOnlyList<string> DefaultExclusionPatterns =
    [
        "**/.git/**",
        "**/node_modules/**",
        "**/bin/**",
        "**/obj/**",
        "**/.vs/**",
    ];

    /// <summary>Defaultiert mit leerer Root-Liste, Standard-Ausschlüssen und ohne UI-Ausschlüsse — Auto-Tagging an.</summary>
    public static IndexingSettings Default => new([], DefaultExclusionPatterns, [], true);
}

/// <summary>Settings für Darstellung (Theme, Schriftgröße, Trefferanzahl).</summary>
/// <param name="Theme">Theme-Wahl.</param>
/// <param name="PreviewFontSize">Schriftgröße der Preview in Pixel.</param>
/// <param name="ResultsPerPage">Anzahl der angezeigten Suchtreffer.</param>
public sealed record AppearanceSettings(
    AppTheme Theme,
    [property: Range(8, 64)] int PreviewFontSize,
    [property: Range(10, 1000)] int ResultsPerPage)
{
    /// <summary>System-Theme, 16 px Preview, 50 Treffer/Seite.</summary>
    public static AppearanceSettings Default => new(AppTheme.System, 16, 50);
}

/// <summary>Settings für Verhalten (Such-Debounce, Indexer-Re-Sync-Intervall, Update-Prüfung).</summary>
/// <param name="SearchDebounceMs">Wartezeit nach letztem Tastendruck, bevor die Suche feuert.</param>
/// <param name="IndexerResyncIntervalSeconds">Soll/Ist-Abgleich des Indexers in Sekunden (<c>0</c> deaktiviert).</param>
/// <param name="CheckForUpdatesAtStartup">Wenn <see langword="true"/>, prüft die Anwendung beim Start auf neue Versionen.</param>
public sealed record BehaviorSettings(
    [property: Range(50, 5_000)] int SearchDebounceMs,
    [property: Range(0, 3_600)] int IndexerResyncIntervalSeconds,
    bool CheckForUpdatesAtStartup = true)
{
    /// <summary>300 ms Such-Debounce, 300 s Indexer-Resync, Update-Prüfung aktiv.</summary>
    public static BehaviorSettings Default => new(300, 300, true);
}

/// <summary>Theme-Wahl für die Darstellung.</summary>
public enum AppTheme
{
    /// <summary>Folgt dem Betriebssystem-Theme.</summary>
    System = 0,

    /// <summary>Helles Theme erzwungen.</summary>
    Light = 1,

    /// <summary>Dunkles Theme erzwungen.</summary>
    Dark = 2,
}
