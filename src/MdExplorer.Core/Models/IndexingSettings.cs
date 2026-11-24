using System.ComponentModel.DataAnnotations;

namespace MdExplorer.Core.Models;

/// <summary>Settings für Index-Roots und Ausschluss-Muster.</summary>
/// <param name="Roots">Wurzelpfade, die rekursiv nach <c>*.md</c> durchsucht werden.</param>
/// <param name="ExclusionPatterns">Glob-Muster für Ausschlüsse (mit <c>!</c>-Negation).</param>
/// <param name="UiExcludedFolders">Absolute Pfade einzelner Ordner, die der Nutzer
/// per Folder-Tree-Kontextmenü aus der Indexierung ausgeschlossen hat.
/// Wirkt additiv zu <paramref name="ExclusionPatterns"/> und <c>.mdignore</c>-Hierarchie.</param>
/// <param name="AutoExtractHashtags">Wenn <see langword="true"/>, extrahiert
/// der Indexer Hashtags aus dem Markdown-Body. Frontmatter-<c>tags</c>-Werte werden
/// unabhängig davon stets übernommen, weil sie explizit gesetzt sind.</param>
public sealed record IndexingSettings(
    IReadOnlyList<string> Roots,
    IReadOnlyList<string> ExclusionPatterns,
    IReadOnlyList<string> UiExcludedFolders,
    bool AutoExtractHashtags)
{
    /// <summary>
    /// Default-Ausschlussmuster.
    /// </summary>
    /// <remarks>
    /// <c>.git</c>, <c>bin</c>, <c>obj</c> und <c>.vs</c> sind Ordner von Versionsverwaltung
    /// und Werkzeugen. <c>vendor</c> hält
    /// Markdown-Dateien fremder Pakete fern: In einem gewachsenen Bestand lagen dort 3.906
    /// davon, Dokumentation, die niemand hier sucht und die jede Trefferliste überlagert. Es
    /// ist der Ordnername, den PHP, Ruby und Go gleichermaßen verwenden; <c>node_modules</c>
    /// ist dasselbe für JavaScript.
    /// </remarks>
    public static readonly IReadOnlyList<string> DefaultExclusionPatterns =
    [
        "**/.git/**",
        "**/node_modules/**",
        "**/vendor/**",
        "**/bin/**",
        "**/obj/**",
        "**/.vs/**",
    ];

    /// <summary>Defaultiert mit leerer Root-Liste, Standard-Ausschlüssen und ohne UI-Ausschlüsse, Auto-Tagging an.</summary>
    public static IndexingSettings Default => new([], DefaultExclusionPatterns, [], true);
}
